# Session Restore + Settings Window Design

**Date:** 2026-09-11
**Status:** Draft (pending user review)
**Scope:** Core (`LogTail.Core`), UI (`LogTail.UI`), Core Tests (`LogTail.Core.Tests`), UI Tests (`LogTail.UI.Tests`)

## Problem

Setiap kali LogTail dijalankan, jendela terbuka kosong dan user harus membuka ulang file log yang sama secara manual. Tab yang sedang dikerjakan pada sesi sebelumnya hilang begitu aplikasi ditutup. Selain itu, preferensi aplikasi tidak punya tempat khusus — Theme hanya bisa diganti lewat menu `View > Theme`, dan tidak ada wadah untuk preferensi lain seperti perilaku startup.

## Goal

1. Saat startup, pulihkan seluruh tab dari sesi terakhir beserta tab yang terakhir aktif.
2. Jangan hanya bergantung pada file terakhir: jika file dari sesi hilang (dipindah/dihapus/drive belum ter-mount), tab tersebut di-skip tetapi path-nya tetap dipertahankan agar bisa ter-restore pada sesi berikutnya.
3. Sediakan opsi on/off untuk perilaku restore, **enabled by default**.
4. Opsi OFF berarti *pause*: berhenti restore dan berhenti menyimpan, tetapi data sesi lama tidak dihapus.
5. Sediakan jendela Settings yang bisa menampung preferensi (dimulai dari Theme yang pindah dari menu `View`, plus opsi restore), dibuka lewat ikon gear di pojok kanan atas.
6. Jika LogTail dijalankan dengan file eksplisit, file eksplisit menang dan session restore di-skip.

## Design

### 1. Model Data & Penyimpanan

`AppSettings` (`src/LogTail.Core/Models/AppSettings.cs`) hanya bertambah satu preferensi:

```csharp
public sealed record AppSettings(
    ThemeMode Theme = ThemeMode.System,
    int BufferCapacity = 50_000,
    int MaxBufferCapacity = 2_000_000,
    TimeSpan PollInterval = default,
    string DefaultEncoding = "utf-8",
    bool RestoreLastSession = true);
```

`RestoreLastSession` default `true`. Deserialisasi `settings.json` lama yang belum punya field ini tetap menghasilkan nilai default (System.Text.Json memakai default parameter constructor).

State sesi yang bersifat volatil disimpan terpisah di `session.json`, satu folder dengan `settings.json` (yaitu `%LocalAppData%/log-tail/`):

```csharp
// src/LogTail.Core/Models/SessionState.cs
public sealed record SessionState(
    IReadOnlyList<string> Files,
    string? ActiveFile);
```

Kelas baru `SessionStore` (`src/LogTail.Core/Persistence/SessionStore.cs`) berbentuk mirror `SettingsStore` (`Load`/`Save`/`Update`), mengembalikan `SessionState` kosong (`Files` kosong, `ActiveFile` null) bila file belum ada atau korup, dan mencatat warning lewat `ILogTailLogger` tanpa crash.

WHY dipisah dari `settings.json`: sesi ditulis setiap perubahan tab. Bila digabung, setiap ganti tab akan menulis ulang Theme/Buffer dan berpotensi balapan tulis dengan perubahan Theme dari jendela Settings. Dua file terpisah berarti dua domain tulis yang independen dan tidak saling menimpa.

### 2. `SessionTracker` (Orkestrasi Save)

Kelas baru `SessionTracker` (`src/LogTail.UI/ViewModels/SessionTracker.cs`, satu tipe per file) dibuat agar `MainWindowViewModel` tidak membengkak:

```csharp
public sealed class SessionTracker : IDisposable
{
    public SessionTracker(
        SessionStore store,
        ObservableCollection<TabViewModel> tabs,
        IObservable<TabViewModel?> selectedTab,
        ILogTailLogger? logger = null);

    public bool IsEnabled { get; set; }
    public void SeedUnresolved(IEnumerable<string> paths);
    public void SaveNow();
    public void Dispose();
}
```

Tingkah laku:

- Subscribe ke `tabs.CollectionChanged` dan perubahan `selectedTab`. Setiap event, jika `IsEnabled`, tulis sesi.
- `SaveNow()` menyusun `Files` = path tab berurutan **digabung** path *unresolved* yang di-seed (dedup case-insensitive), dan `ActiveFile` = `SelectedTab?.FilePath`.
- `IsEnabled = false` (opsi OFF) → tidak baca, tidak tulis. Data lama di disk dibiarkan utuh.
- Toggle OFF → ON hanya resume; tidak langsung snapshot. Tulis baru terjadi saat ada perubahan tab berikutnya. Ini memenuhi pilihan "OFF = pause": jika di-ON lalu restart tanpa perubahan tab, sesi lama masih dipulihkan.
- Path unresolved tidak pernah menjadi tab, sehingga selalu ikut tersimpan pada `SaveNow()` berikutnya (tidak terhapus) — inilah yang membuat file hilang tetap bisa kembali.

### 3. Startup Restore & Retensi File Hilang

Di `src/LogTail.UI/App.axaml.cs` (`OnFrameworkInitializationCompleted`), urutannya:

1. Deteksi `explicitFile` (saat ini dari env `LOGTAIL_AUTO_OPEN_FILE`; ke depan dari CLI arg). Jika ada dan file-nya valid → buka file itu saja, **skip** session restore (memenuhi "file eksplisit menang"). Membuka file eksplisit memicu save biasa, jadi sesi tersimpan berganti menjadi file tersebut — wajar karena sesi merefleksikan apa yang sedang dipakai.
2. Jika tidak ada `explicitFile` **dan** `RestoreLastSession == true`:
   - `sessionStore.Load()`.
   - Bagi `Files` menjadi valid / hilang memakai `LogFileValidator.TryValidateFile`.
   - Panggil `MainWindowViewModel.RestoreTabs(validPaths, activePath)` (lihat bagian 4) lalu `tracker.SeedUnresolved(missingPaths)`.
   - Jika ada yang hilang, set `StatusMessage`, mis. `"2 file dari sesi terakhir tidak ditemukan"`.
3. Jika `RestoreLastSession == false` → tidak membaca sesi sama sekali. `SessionTracker` tetap dibuat tetapi `IsEnabled = false`, jadi tidak ada restore maupun save sampai opsi dinyalakan.

### 4. Perubahan `MainWindowViewModel`

`src/LogTail.UI/ViewModels/MainWindowViewModel.cs`:

- Tambah method batch `internal void RestoreTabs(IReadOnlyList<string> paths, string? activePath)`:
  - Tambahkan seluruh `TabViewModel` (masing-masing lewat `AttachLinesPerSecondCounter`) **tanpa** men-set `SelectedTab` per item.
  - Set `SelectedTab` **sekali** di akhir ke tab yang cocok dengan `activePath`, atau tab valid terakhir bila `activePath` tidak ada di daftar. Ini menghindari N-kali start/stop tail karena observer `SelectedTab` sudah ada.
- Tambah `Interaction<Unit, Unit> ShowSettingsDialog` + `OpenSettingsCommand` (pola sama dengan `ShowOpenFileDialog`).
- Tambah properti `SettingsViewModel Settings { get; }` (di-inject lewat constructor).
- **Hapus** `CurrentTheme` dan `SetThemeCommand` — setelah menu `View > Theme` dihapus, keduanya tidak lagi dipakai view; kepemilikan Theme pindah ke `SettingsViewModel`.

### 5. `SettingsViewModel` + Jendela Settings

Kelas baru `SettingsViewModel` (`src/LogTail.UI/ViewModels/SettingsViewModel.cs`):

```csharp
public sealed class SettingsViewModel : ReactiveObject
{
    public SettingsViewModel(SettingsStore settings, ILogTailLogger? logger = null);

    public ThemeMode Theme { get; set; }              // set → persist + raise + event
    public bool RestoreLastSession { get; set; }      // set → persist + raise + event
    public IReadOnlyList<ThemeMode> ThemeOptions { get; }  // [System, Light, Dark]

    public event Action<ThemeMode>? ThemeChanged;
    public event Action<bool>? RestoreLastSessionChanged;
}
```

Setiap setter memanggil `SettingsStore.Update` dan memicu event terkait. Nilai awal dibaca dari `AppSettings` yang tersimpan.

Jendela baru `SettingsWindow.axaml` + `SettingsWindow.axaml.cs` (`src/LogTail.UI/Views/`): dialog modal tanpa resizing, berisi:
- ComboBox Theme (`ItemsSource = ThemeOptions`, `SelectedItem` two-way ke `Theme`).
- CheckBox "Reopen last session on startup" two-way ke `RestoreLastSession`.
- Tombol Close.

Perubahan Theme langsung diterapkan (live) seperti perilaku menu sebelumnya.

### 6. Layout Gear + Wiring `App`

`src/LogTail.UI/Views/MainWindow.axaml`:
- Baris atas diubah menjadi `Grid ColumnDefinitions="*,Auto"` — `Menu` di kolom 0, tombol gear (`PathIcon` dengan geometri gear) di kolom 1 (pojok kanan atas).
- Hapus submenu `View` seluruhnya (Theme pindah ke Settings). Menu yang tersisa hanya `File` (Open…, Separator, Exit).

`src/LogTail.UI/Views/MainWindow.axaml.cs`:
- Daftarkan handler `ShowSettingsDialog` di `WhenActivated`, buka `SettingsWindow` dengan `ViewModel.Settings` sebagai DataContext lewat `ShowDialog(this)`.

`src/LogTail.UI/App.axaml.cs`:
- Buat satu `SettingsViewModel` bersama, `ApplyTheme(settingsVm.Theme)` saat start, subscribe `ThemeChanged → ApplyTheme`.
- Buat `SessionStore` + `SessionTracker` (`IsEnabled = settingsVm.RestoreLastSession`, sumber observasi `viewModel.Tabs` dan `viewModel.WhenAnyValue(x => x.SelectedTab)`).
- Subscribe `RestoreLastSessionChanged → tracker.IsEnabled`.
- Dispose tracker pada `desktop.Exit`.

WHY Theme dipusatkan di `SettingsViewModel`: hanya satu pemilik state Theme, sehingga `App` mengaplikasikan tema dari satu sumber dan tidak ada dua jalur yang bisa tidak sinkron.

## Error Handling

- `session.json` korup / tidak bisa dibaca → `SessionState` kosong + warning log, aplikasi tetap jalan.
- Gagal menulis `session.json` → warning log, diabaikan (tidak mengganggu tailing).
- Semua path restore invalid → jendela kosong + `StatusMessage` memberi tahu.
- Opsi OFF → tidak restore, tidak save, data lama utuh.
- `SettingsStore`/`SessionStore` masing-masing hanya menulis file-nya sendiri; `SessionTracker` dipanggil dari UI thread, jadi tidak ada balapan tulis.

## Testing

Automated:

Core (`LogTail.Core.Tests`):
1. `SessionStore`: `Load` saat file belum ada → `SessionState` kosong.
2. `SessionStore`: `Save` lalu `Load` → roundtrip `Files` dan `ActiveFile`.
3. `SessionStore`: file korup → `SessionState` kosong.
4. `AppSettings`: `RestoreLastSession` default `true`.
5. `AppSettings`: roundtrip nilai `RestoreLastSession = false`.

UI (`LogTail.UI.Tests`):
6. `SettingsViewModel`: Theme awal terbaca dari store default (`System`).
7. `SettingsViewModel`: set `Theme` → tersimpan di `settings.json` + `ThemeChanged` ter-raise.
8. `SettingsViewModel`: set `RestoreLastSession` → tersimpan + `RestoreLastSessionChanged` ter-raise.
9. `SessionTracker`: `IsEnabled = true` + perubahan koleksi tab → `session.json` memuat path tab.
10. `SessionTracker`: perubahan `SelectedTab` → `ActiveFile` ikut ter-update.
11. `SessionTracker`: `IsEnabled = false` → tidak ada tulisan sama sekali.
12. `SessionTracker`: `SeedUnresolved` → path hilang tetap ada di sesi bareng tab valid.
13. `MainWindowViewModel.RestoreTabs`: menambah tab sesuai urutan, memilih active yang benar.
14. Update tes lama yang menyentuh `CurrentTheme`/`SetThemeCommand` agar menargetkan `SettingsViewModel`.

Manual smoke:
- Klik gear → jendela Settings terbuka; ganti Theme → tampilan langsung berubah.
- Toggle restore OFF → restart → jendela kosong; ON → restart → tab kembali.
- Hapus salah satu file sesi lalu restart → tab itu hilang, tab lain kembali, `session.json` masih menyimpan path yang hilang.
- Jalankan dengan file eksplisit (env `LOGTAIL_AUTO_OPEN_FILE`) → hanya file itu yang terbuka, sesi tidak di-restore.

## Out of Scope

- Restore ukuran/posisi window, posisi scroll viewer, dan state AutoScroll per tab.
- Menu riwayat / recent files.
- Tombol "Clear session" (bisa menyusul).
- Implementasi CLI args (`--new-window`, wildcard, folder) — ada spec terpisah.
- Settings non-modal atau lebih dari satu jendela Settings.
- Pemangkasan otomatis path yang hilang permanen.

## Risks

1. **Path hilang menumpuk permanen:** karena dipertahankan sengaja, `session.json` bisa menyimpan path mati selamanya. Diterima untuk iterasi ini; pemangkasan manual bisa ditambah nanti.
2. **Urutan berubah:** saat save, path unresolved ditempatkan setelah path tab, jadi urutan tab yang dipulihkan bisa berbeda dari sesi asli bila ada file hilang di tengah. Dampak kosmetik.
3. **Perubahan kontrak VM:** `CurrentTheme`/`SetThemeCommand` dihapus dari `MainWindowViewModel`, sehingga tes lama harus disesuaikan. Ini konsekuensi langsung dari pemindahan Theme yang diminta.

## Implementation Order

1. Core: `SessionState` record + field `RestoreLastSession` di `AppSettings`.
2. Core: `SessionStore` (Load/Save/Update) + tes Core.
3. UI: `SettingsViewModel` + tes.
4. UI: `SessionTracker` + tes.
5. UI: `MainWindowViewModel.RestoreTabs`, `Settings`, `OpenSettingsCommand`, hapus `CurrentTheme`/`SetThemeCommand`; sesuaikan tes lama.
6. UI: `SettingsWindow` + gear di `MainWindow`, hapus menu `View`.
7. UI: wiring `App` (theme + session restore + tracker + event).
8. Manual smoke sesuai daftar di atas.
