using System;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LogTail.Core.Models;
using LogTail.UI.ViewModels;
using ReactiveUI;

namespace LogTail.UI.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private ScrollViewer? _scrollViewer;
    private bool _isProgrammaticScroll;
    private bool _scrollRequested;

    public MainWindow()
    {
        InitializeComponent();

        // DragDrop handlers for dropping files from OS file manager
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

        this.WhenActivated(disposables =>
        {
            // Register ReactiveUI Interaction handler for File Picker dialog
            ViewModel?.ShowOpenFileDialog.RegisterHandler(DoShowOpenFileDialogAsync)
                .DisposeWith(disposables);

            // Subscribe to ViewModel AutoScroll changes using WhenAnyValue
            this.WhenAnyValue(x => x.ViewModel!.AutoScroll)
                .Where(autoScroll => autoScroll)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RequestScrollToBottom())
                .DisposeWith(disposables);

            // Subscribe to collection changes for auto-scroll on new log items
            if (ViewModel != null)
            {
                this.WhenAnyValue(x => x.ViewModel!.SelectedTab)
                    .DistinctUntilChanged()
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ => AttachSelectedTabScrollListener(disposables))
                    .DisposeWith(disposables);
            }

            AttachScrollViewerListener(disposables);
        });
    }

    private void AttachScrollViewerListener(CompositeDisposable disposables)
    {
        // Scroll-viewer detection is wired per active tab in AttachSelectedTabScrollListener.
    }

    private void AttachSelectedTabScrollListener(CompositeDisposable disposables)
    {
        if (ViewModel?.SelectedTab == null)
        {
            return;
        }

        var tab = ViewModel.SelectedTab;
        var tabDisposables = new CompositeDisposable();

        // Auto-scroll to bottom on new log events for this tab.
        Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => tab.LogEvents.CollectionChanged += h,
                h => tab.LogEvents.CollectionChanged -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ =>
            {
                if (ViewModel is { AutoScroll: true } && tab.LogEvents.Count > 0)
                {
                    RequestScrollToBottom();
                }
            })
            .DisposeWith(tabDisposables);

        Dispatcher.UIThread.Post(() =>
        {
            var scrollViewer = this.FindControl<TabControl>("MainTabControl")?
                .FindDescendantOfType<ScrollViewer>();
            if (scrollViewer == null)
            {
                return;
            }

            _scrollViewer = scrollViewer;
            scrollViewer.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(OnScrollOffsetChanged)
                .DisposeWith(tabDisposables);
        }, DispatcherPriority.Background);

        tabDisposables.DisposeWith(disposables);
    }

    private void OnScrollOffsetChanged(Vector offset)
    {
        if (_scrollViewer == null || _isProgrammaticScroll || ViewModel == null) return;

        var extentHeight = _scrollViewer.Extent.Height;
        var viewportHeight = _scrollViewer.Viewport.Height;

        // If content fits completely within viewport, there is nothing to scroll.
        // Respect the user's explicit AutoScroll choice — do not force it on.
        if (extentHeight <= viewportHeight)
        {
            return;
        }

        var maxOffsetY = extentHeight - viewportHeight;
        // Toleransi 15 pixel dari dasar scroll
        var isAtBottom = offset.Y >= maxOffsetY - 15.0;

        if (ViewModel.AutoScroll != isAtBottom)
        {
            ViewModel.AutoScroll = isAtBottom;
        }
    }

    private void RequestScrollToBottom()
    {
        if (ViewModel == null || !ViewModel.AutoScroll || _scrollRequested)
        {
            return;
        }

        _scrollRequested = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollRequested = false;
            if (ViewModel is { AutoScroll: true })
            {
                ScrollToBottom();
            }
        }, DispatcherPriority.Background);
    }

    private void ScrollToBottom()
    {
        if (ViewModel?.SelectedTab == null || ViewModel.SelectedTab.LogEvents.Count == 0) return;

        _isProgrammaticScroll = true;
        try
        {
            var scrollViewer = this.FindControl<TabControl>("MainTabControl")?
                .FindDescendantOfType<ScrollViewer>();
            scrollViewer?.ScrollToEnd();
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _isProgrammaticScroll = false, DispatcherPriority.Normal);
        }
    }

    private async Task DoShowOpenFileDialogAsync(IInteractionContext<Unit, string?> interaction)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select log file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log files") { Patterns = new[] { "*.log", "*.txt", "*.*" } }
            }
        });

        var selectedPath = result.Count > 0 ? result[0].Path.LocalPath : null;
        interaction.SetOutput(selectedPath);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer is { } dt && dt.TryGetFiles() is { Length: > 0 })
        {
            e.DragEffects = DragDropEffects.Copy;
            Classes.Add("drag-over");
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            Classes.Remove("drag-over");
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        Classes.Remove("drag-over");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        Classes.Remove("drag-over");

        var dt = e.DataTransfer;
        if (ViewModel == null || dt == null)
        {
            return;
        }

        var files = dt.TryGetFiles();
        if (files == null)
        {
            return;
        }

        var vm = ViewModel;
        if (vm == null)
        {
            return;
        }

        foreach (var file in files)
        {
            if (file is not IStorageFile storageFile)
            {
                // Folders arrive as IStorageFolder; silently ignore per spec.
                continue;
            }

            var path = storageFile.Path.LocalPath;
            if (LogFileValidator.TryValidateFile(path, out var error))
            {
                vm.AddTab(path);
            }
            else if (error != null)
            {
                vm.StatusMessage = error;
            }
        }
    }

    private async void OnTabLogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox ||
            e.GetCurrentPoint(listBox).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        // Build text from all currently-selected log lines.
        var lines = (listBox.SelectedItems ?? Array.Empty<object?>())
            .OfType<EnrichedLogEvent>()
            .Select(ev => ev.Raw.Line)
            .ToList();

        if (lines.Count == 0)
        {
            return;
        }

        var payload = string.Join(Environment.NewLine, lines);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(payload));

        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }
}
