using System;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
                Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                        h => ViewModel.VisibleEvents.CollectionChanged += h,
                        h => ViewModel.VisibleEvents.CollectionChanged -= h)
                    .ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(_ =>
                    {
                        if (ViewModel is { AutoScroll: true, VisibleEvents.Count: > 0 })
                        {
                            RequestScrollToBottom();
                        }
                    })
                    .DisposeWith(disposables);
            }

            AttachScrollViewerListener(disposables);
        });
    }

    private void AttachScrollViewerListener(CompositeDisposable disposables)
    {
        _scrollViewer = LogListBox.FindDescendantOfType<ScrollViewer>();
        if (_scrollViewer != null)
        {
            _scrollViewer.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(OnScrollOffsetChanged)
                .DisposeWith(disposables);
        }
        else
        {
            // If template not fully materialized yet, subscribe once attached to visual tree
            LogListBox.TemplateApplied += (_, _) =>
            {
                _scrollViewer = LogListBox.FindDescendantOfType<ScrollViewer>();
                _scrollViewer?.GetObservable(ScrollViewer.OffsetProperty)
                    .Subscribe(OnScrollOffsetChanged)
                    .DisposeWith(disposables);
            };
        }
    }

    private void OnScrollOffsetChanged(Vector offset)
    {
        if (_scrollViewer == null || _isProgrammaticScroll || ViewModel == null) return;

        var extentHeight = _scrollViewer.Extent.Height;
        var viewportHeight = _scrollViewer.Viewport.Height;

        // If content fits completely within viewport, auto-scroll stays enabled.
        if (extentHeight <= viewportHeight)
        {
            if (!ViewModel.AutoScroll) ViewModel.AutoScroll = true;
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
        if (ViewModel == null || !ViewModel.AutoScroll || ViewModel.VisibleEvents.Count == 0 || _scrollRequested)
        {
            return;
        }

        _scrollRequested = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollRequested = false;
            if (ViewModel is { AutoScroll: true, VisibleEvents.Count: > 0 })
            {
                ScrollToBottom();
            }
        }, DispatcherPriority.Background);
    }

    private void ScrollToBottom()
    {
        if (ViewModel == null || ViewModel.VisibleEvents.Count == 0) return;

        _isProgrammaticScroll = true;
        try
        {
            LogListBox.ScrollIntoView(ViewModel.VisibleEvents.Count - 1);
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
}
