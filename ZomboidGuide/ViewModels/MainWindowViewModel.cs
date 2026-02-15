using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int CurrentInventoryDetectionVersion = 5;

    private readonly AppStateService _appStateService = new();
    private readonly GuideCatalogService _guideCatalogService = new();
    private readonly SessionSyncService _sessionSyncService = new();
    private readonly AppUpdateService _appUpdateService = new();

    private readonly DispatcherTimer _sessionTimer = new() { Interval = TimeSpan.FromMinutes(2) };
    private readonly DispatcherTimer _sessionWatcherDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };

    private readonly List<GuideItem> _catalogItems = [];
    private readonly List<ChecklistItemViewModel> _allItems = [];
    private readonly Dictionary<string, ChecklistItemViewModel> _itemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChecklistItemViewModel> _bookItems = [];
    private readonly List<ChecklistItemViewModel> _magazineItems = [];
    private readonly List<ChecklistItemViewModel> _recipeItems = [];
    private readonly Dictionary<string, bool> _bookGroupExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _magazineGroupExpandedStates = new(StringComparer.OrdinalIgnoreCase);

    private AppState _state = new();
    private UpdateCheckResult? _latestUpdateResult;
    private bool _suppressItemStateWrite;
    private bool _sessionSyncRunning;
    private bool _suppressLanguageReload;
    private bool _suppressStatusFilterReload;
    private bool _isInitializing;
    private FileSystemWatcher? _sessionWatcher;
    private bool _sessionWatcherDirty;
    private string _watchedSavePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BookCategoryGroupViewModel> filteredBookGroups = [];

    [ObservableProperty]
    private ObservableCollection<BookCategoryGroupViewModel> filteredMagazineGroups = [];

    [ObservableProperty]
    private ObservableCollection<ChecklistItemViewModel> filteredRecipes = [];

    [ObservableProperty]
    private ObservableCollection<SessionSkillLevelViewModel> sessionSkills = [];

    [ObservableProperty]
    private string sessionSkillsHeader = "Session Skills (0)";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string gamePath = string.Empty;

    [ObservableProperty]
    private bool includeMods = true;

    [ObservableProperty]
    private ObservableCollection<LanguageOptionViewModel> availableLanguages = [];

    [ObservableProperty]
    private LanguageOptionViewModel? selectedLanguage;

    [ObservableProperty]
    private ObservableCollection<StatusFilterOptionViewModel> availableBookStatusFilters = [];

    [ObservableProperty]
    private ObservableCollection<StatusFilterOptionViewModel> availableMagazineStatusFilters = [];

    [ObservableProperty]
    private ObservableCollection<StatusFilterOptionViewModel> availableRecipeStatusFilters = [];

    [ObservableProperty]
    private StatusFilterOptionViewModel? selectedBookStatusFilter;

    [ObservableProperty]
    private StatusFilterOptionViewModel? selectedMagazineStatusFilter;

    [ObservableProperty]
    private StatusFilterOptionViewModel? selectedRecipeStatusFilter;

    [ObservableProperty]
    private bool autoSessionSync;

    [ObservableProperty]
    private bool autoUpdateCheck = true;

    [ObservableProperty]
    private string updateFeedPath = string.Empty;

    [ObservableProperty]
    private string statusMessage = "Loading data ...";

    [ObservableProperty]
    private string updateStatusMessage = "Update: not checked yet";

    [ObservableProperty]
    private bool isUpdateAvailable;

    [ObservableProperty]
    private string dataSource = "Not loaded yet";

    [ObservableProperty]
    private string lastSyncText = "No catalog sync yet";

    [ObservableProperty]
    private string lastSessionSyncText = "No session sync yet";

    [ObservableProperty]
    private string bookProgress = "0 / 0";

    [ObservableProperty]
    private string magazineProgress = "0 / 0";

    [ObservableProperty]
    private string recipeProgress = "0 / 0";

    [ObservableProperty]
    private bool isBusy;

    public string HeaderTitleText => "MietzeMatze's Zomboid Guide";

    public string HeaderSubtitleText => L(
        "Track inventory books and view current session skills with levels.",
        "Inventar-Bücher tracken und Session-Skills mit Stufe anzeigen.");

    public string GamePathWatermarkText => L(
        @"Path to ...\steamapps\common\ProjectZomboid",
        @"Pfad zu ...\steamapps\common\ProjectZomboid");

    public string ModsOffText => L("Mods Off", "Mods aus");

    public string ModsOnText => L("Mods On", "Mods an");

    public string AutoDetectPathButtonText => L("Detect Path", "Pfad erkennen");

    public string LoadFromGameButtonText => L("Load From Game", "Aus Spiel laden");

    public string LoadFallbackButtonText => L("Load Fallback", "Fallback laden");

    public string SyncSessionButtonText => L("Load Active Session", "Aktive Session laden");

    public string SearchWatermarkText => L(
        "Search skills, books, magazines, recipes ...",
        "Suche in Skills, Büchern, Magazinen, Rezepten ...");

    public string AutoSessionSyncOffText => L("Auto Session Sync Off", "Auto-Session-Sync aus");

    public string AutoSessionSyncOnText => L("Auto Session Sync On", "Auto-Session-Sync an");

    public string ClearChecksButtonText => L("Clear All Checks", "Alle Haken entfernen");

    public string UpdateFeedPathWatermarkText => L(
        "Update source: local folder, manifest.json or GitHub repo (owner/repo)",
        "Update-Quelle: lokaler Ordner, manifest.json oder GitHub-Repo (owner/repo)");

    public string AutoUpdateOffText => L("Auto Update Check Off", "Auto-Updatecheck aus");

    public string AutoUpdateOnText => L("Auto Update Check On", "Auto-Updatecheck an");

    public string CheckUpdatesButtonText => L("Check For Update", "Nach Update suchen");

    public string InstallUpdateButtonText => L("Install Update", "Update installieren");

    public string BooksTabHeader => $"{L("Books", "Bücher")} ({BookProgress})";

    public string MagazinesTabHeader => $"{L("Magazines", "Magazine")} ({MagazineProgress})";

    public string RecipesTabHeader => $"{L("Recipes", "Rezepte")} ({RecipeProgress})";

    public string CopyrightLabelText => "Copyright (c)";

    public string TwitchButtonText => L("MietzeMatze on Twitch", "MietzeMatze auf Twitch");

    public string LanguageLabelText => L("Language", "Sprache");

    public string BookFilterLabelText => L("Books Filter", "Buecher-Filter");

    public string MagazineFilterLabelText => L("Magazines Filter", "Magazine-Filter");

    public string RecipeFilterLabelText => L("Recipes Filter", "Rezepte-Filter");

    public MainWindowViewModel()
    {
        _sessionTimer.Tick += SessionTimerOnTick;
        _sessionWatcherDebounceTimer.Tick += SessionWatcherDebounceOnTick;
        _sessionTimer.Start();
        _ = InitializeAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnGamePathChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        _state.GamePath = value;
        _ = RefreshAvailableLanguagesAsync();
        ConfigureSessionWatcher();
        _ = SaveStateAsync();
    }

    partial void OnIncludeModsChanged(bool value)
    {
        if (_isInitializing)
        {
            return;
        }

        _state.IncludeMods = value;
        _ = RefreshAvailableLanguagesAsync();
        ConfigureSessionWatcher();
        _ = SaveStateAsync();
    }

    partial void OnSelectedLanguageChanged(LanguageOptionViewModel? value)
    {
        if (_suppressLanguageReload || _isInitializing)
        {
            return;
        }

        var languageCode = value?.Code ?? "EN";
        _state.LanguageCode = languageCode;
        ApplyUiLanguage();
        _ = SaveStateAsync();
        _ = ReloadAndResyncForLanguageChangeAsync();
    }

    partial void OnSelectedBookStatusFilterChanged(StatusFilterOptionViewModel? value)
    {
        if (_suppressStatusFilterReload || _isInitializing)
        {
            return;
        }

        _state.BookStatusFilterKey = value?.Key ?? "all";
        ApplyFilters();
        _ = SaveStateAsync();
    }

    partial void OnSelectedMagazineStatusFilterChanged(StatusFilterOptionViewModel? value)
    {
        if (_suppressStatusFilterReload || _isInitializing)
        {
            return;
        }

        _state.MagazineStatusFilterKey = value?.Key ?? "all";
        ApplyFilters();
        _ = SaveStateAsync();
    }

    partial void OnSelectedRecipeStatusFilterChanged(StatusFilterOptionViewModel? value)
    {
        if (_suppressStatusFilterReload || _isInitializing)
        {
            return;
        }

        _state.RecipeStatusFilterKey = value?.Key ?? "all";
        ApplyFilters();
        _ = SaveStateAsync();
    }

    partial void OnAutoSessionSyncChanged(bool value)
    {
        _state.AutoSessionSync = value;
        _ = SaveStateAsync();
    }

    partial void OnAutoUpdateCheckChanged(bool value)
    {
        _state.AutoUpdateCheck = value;
        _ = SaveStateAsync();
    }

    partial void OnUpdateFeedPathChanged(string value)
    {
        _state.UpdateFeedPath = value;
        _ = SaveStateAsync();
    }

    partial void OnBookProgressChanged(string value)
    {
        OnPropertyChanged(nameof(BooksTabHeader));
    }

    partial void OnMagazineProgressChanged(string value)
    {
        OnPropertyChanged(nameof(MagazinesTabHeader));
    }

    partial void OnRecipeProgressChanged(string value)
    {
        OnPropertyChanged(nameof(RecipesTabHeader));
    }

    [RelayCommand]
    private async Task RefreshFromGameAsync()
    {
        await ReloadDataAsync(preferGameFiles: true);
    }

    [RelayCommand]
    private async Task LoadDefaultsAsync()
    {
        await ReloadDataAsync(preferGameFiles: false);
    }

    [RelayCommand]
    private async Task SyncActiveSessionAsync()
    {
        await SyncSessionAsync(isManual: true);
    }

    [RelayCommand]
    private async Task AutoDetectPathAsync()
    {
        var detectedPath = _guideCatalogService.TryAutoDetectGamePath();
        if (string.IsNullOrWhiteSpace(detectedPath))
        {
            StatusMessage = L("Could not auto-detect a Project Zomboid installation.", "Keine Project-Zomboid-Installation automatisch gefunden.");
            return;
        }

        GamePath = detectedPath;
        StatusMessage = L($"Path detected: {detectedPath}", $"Pfad erkannt: {detectedPath}");
        await SaveStateAsync();
    }

    [RelayCommand]
    private void OpenCopyrightLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.twitch.tv/MietzeMatze",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusMessage = L($"Could not open link: {exception.Message}", $"Konnte Link nicht öffnen: {exception.Message}");
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateFeedPath))
        {
            IsUpdateAvailable = false;
            _latestUpdateResult = null;
            UpdateStatusMessage = L("No update source configured.", "Keine Update-Quelle gesetzt.");
            return;
        }

        var currentVersion = GetCurrentAppVersion();
        var result = await Task.Run(() => _appUpdateService.CheckForUpdate(UpdateFeedPath, currentVersion));
        _state.LastUpdateCheckAt = DateTimeOffset.Now;
        await SaveStateAsync();

        if (!result.Success)
        {
            IsUpdateAvailable = false;
            _latestUpdateResult = null;
            UpdateStatusMessage = result.Message;
            return;
        }

        _latestUpdateResult = result;
        IsUpdateAvailable = result.UpdateAvailable;
        if (result.UpdateAvailable && result.AvailableVersion is not null)
        {
            UpdateStatusMessage = L(
                $"Update available: {result.AvailableVersion} (current {currentVersion})",
                $"Update verfügbar: {result.AvailableVersion} (aktuell {currentVersion})");
        }
        else
        {
            UpdateStatusMessage = L($"Already up to date ({currentVersion})", $"Bereits aktuell ({currentVersion})");
        }
    }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (_latestUpdateResult is null || !_latestUpdateResult.UpdateAvailable)
        {
            UpdateStatusMessage = L("No update available to install.", "Kein Update zum Installieren gefunden.");
            return;
        }

        if (!_appUpdateService.TryStartUpdate(_latestUpdateResult, out var errorMessage))
        {
            UpdateStatusMessage = errorMessage;
            return;
        }

        UpdateStatusMessage = L(
            "Update installation started. Restarting app ...",
            "Update-Installation gestartet. App wird neu gestartet ...");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.Shutdown();
            return;
        }

        Environment.Exit(0);
    }

    [RelayCommand]
    private async Task UncheckAllAsync()
    {
        _suppressItemStateWrite = true;
        try
        {
            foreach (var item in _allItems)
            {
                item.IsChecked = false;
            }
        }
        finally
        {
            _suppressItemStateWrite = false;
        }

        _state.CheckedItems.Clear();
        _state.SeenInInventoryItemIds.Clear();
        _state.CurrentInventoryItemIds.Clear();
        UpdateProgress();
        await SaveStateAsync();
        StatusMessage = L("All checklists have been reset.", "Alle Checklisten wurden zurückgesetzt.");
    }

    private async Task InitializeAsync()
    {
        _isInitializing = true;
        try
        {
            _state = await _appStateService.LoadAsync();
            _state.SeenInInventoryItemIds ??= [];
            _state.CurrentInventoryItemIds ??= [];
            _state.KnownCatalogItemIds ??= [];
            if (_state.InventoryDetectionVersion < CurrentInventoryDetectionVersion)
            {
                _state.SeenInInventoryItemIds.Clear();
                _state.CurrentInventoryItemIds.Clear();
                _state.AutoSessionSync = true;
                _state.InventoryDetectionVersion = CurrentInventoryDetectionVersion;
                await SaveStateAsync();
            }

            IncludeMods = _state.IncludeMods;
            AutoSessionSync = _state.AutoSessionSync;
            AutoUpdateCheck = _state.AutoUpdateCheck;
            UpdateFeedPath = _state.UpdateFeedPath ?? string.Empty;
            GamePath = _state.GamePath ?? string.Empty;
            _state.LanguageCode = string.IsNullOrWhiteSpace(_state.LanguageCode)
                ? "EN"
                : _state.LanguageCode.ToUpperInvariant();
            _state.BookStatusFilterKey = string.IsNullOrWhiteSpace(_state.BookStatusFilterKey)
                ? "all"
                : _state.BookStatusFilterKey.ToLowerInvariant();
            _state.MagazineStatusFilterKey = string.IsNullOrWhiteSpace(_state.MagazineStatusFilterKey)
                ? "all"
                : _state.MagazineStatusFilterKey.ToLowerInvariant();
            _state.RecipeStatusFilterKey = string.IsNullOrWhiteSpace(_state.RecipeStatusFilterKey)
                ? "all"
                : _state.RecipeStatusFilterKey.ToLowerInvariant();
            ApplyUiLanguage();

            LastSyncText = _state.LastSyncAt.HasValue
                ? L($"Last catalog sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}", $"Letzte Katalog-Sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}")
                : L("No catalog sync yet", "Noch keine Katalog-Synchronisierung");

            LastSessionSyncText = _state.LastSessionSyncAt.HasValue
                ? L($"Last session sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}", $"Letzte Session-Sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}")
                : L("No session sync yet", "Noch keine Session-Synchronisierung");

            if (string.IsNullOrWhiteSpace(GamePath))
            {
                var detectedPath = _guideCatalogService.TryAutoDetectGamePath();
                if (!string.IsNullOrWhiteSpace(detectedPath))
                {
                    GamePath = detectedPath;
                }
            }

            await RefreshAvailableLanguagesAsync();
            await ReloadDataAsync(preferGameFiles: !string.IsNullOrWhiteSpace(GamePath));
            ConfigureSessionWatcher();
            await TrySyncSessionOnStartupAsync();
            if (AutoUpdateCheck && !string.IsNullOrWhiteSpace(UpdateFeedPath))
            {
                await CheckUpdatesAsync();
            }
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private async Task ReloadDataAsync(bool preferGameFiles)
    {
        IsBusy = true;
        StatusMessage = preferGameFiles
            ? L("Reading Zomboid files and building catalog ...", "Lese Zomboid-Dateien und baue Katalog ...")
            : L("Loading default catalog ...", "Lade Standard-Katalog ...");

        try
        {
            var catalog = await _guideCatalogService.LoadAsync(GamePath, IncludeMods, preferGameFiles, _state.LanguageCode);
            RebuildItems(catalog);

            var currentCatalogIds = catalog.Items
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var knownIdsSet = _state.KnownCatalogItemIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hadKnownCatalog = knownIdsSet.Count > 0;
            var newCatalogIds = currentCatalogIds
                .Where(id => !knownIdsSet.Contains(id))
                .ToList();
            _state.KnownCatalogItemIds = currentCatalogIds;

            DataSource = catalog.LoadedFromGameFiles
                ? L("Source: Game files + Mod files", "Quelle: Spieldateien + Mod-Dateien")
                : L("Source: Built-in fallback data", "Quelle: Integrierte Standarddaten");

            _state.GamePath = GamePath;
            _state.IncludeMods = IncludeMods;
            _state.LastSyncAt = DateTimeOffset.Now;
            LastSyncText = L(
                $"Last catalog sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}",
                $"Letzte Katalog-Sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}");

            await SaveStateAsync();
            StatusMessage = hadKnownCatalog && newCatalogIds.Count > 0
                ? L($"Catalog loaded: {_allItems.Count} entries ({newCatalogIds.Count} new)",
                    $"Katalog geladen: {_allItems.Count} Einträge ({newCatalogIds.Count} neu)")
                : L($"Catalog loaded: {_allItems.Count} entries",
                    $"Katalog geladen: {_allItems.Count} Einträge");
        }
        catch (Exception exception)
        {
            StatusMessage = L($"Failed to load data: {exception.Message}", $"Fehler beim Laden: {exception.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncSessionAsync(bool isManual)
    {
        if (_sessionSyncRunning || _catalogItems.Count == 0)
        {
            return;
        }

        ConfigureSessionWatcher();
        _sessionSyncRunning = true;
        try
        {
            if (isManual)
            {
                StatusMessage = L("Reading active session ...", "Lese aktive Session ...");
            }

            var result = await Task.Run(() => _sessionSyncService.SyncFromCurrentSession(_catalogItems));
            if (!result.Success)
            {
                if (isManual)
                {
                    StatusMessage = L($"Session sync failed: {result.Message}", result.Message);
                }

                return;
            }

            ApplySessionStatuses(
                result.CheckedBookItemIds,
                result.ReadBookItemIds,
                result.ObsoleteBookItemIds,
                result.CheckedMagazineItemIds,
                result.ReadMagazineItemIds,
                result.LearnedRecipeItemIds,
                result.SkillLevels);
            ApplySessionSkills(result.SkillLevels);

            _state.LastSessionSyncAt = DateTimeOffset.Now;
            LastSessionSyncText = L(
                $"Last session sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}",
                $"Letzte Session-Sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}");
            await SaveStateAsync();

            StatusMessage = L(
                $"Session synced successfully ({result.PlayerName})",
                $"{result.Message} ({result.PlayerName})");
        }
        finally
        {
            _sessionSyncRunning = false;
        }
    }

    private void ApplySessionStatuses(
        IReadOnlyCollection<string> inventoryBookIds,
        IReadOnlyCollection<string> readBookIds,
        IReadOnlyCollection<string> obsoleteBookIds,
        IReadOnlyCollection<string> inventoryMagazineIds,
        IReadOnlyCollection<string> readMagazineIds,
        IReadOnlyCollection<string> learnedRecipeIds,
        IReadOnlyCollection<SessionSkillLevel> skills)
    {
        _suppressItemStateWrite = true;
        try
        {
            var currentInventorySet = inventoryBookIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _state.CurrentInventoryItemIds = currentInventorySet.ToList();

            var skillsMap = skills.ToDictionary(skill => NormalizeSkillKey(skill.Name), skill => skill.Level, StringComparer.OrdinalIgnoreCase);

            foreach (var book in _bookItems)
            {
                var inInventory = currentInventorySet.Contains(book.Id);
                var isObsolete = obsoleteBookIds.Contains(book.Id, StringComparer.OrdinalIgnoreCase);

                if (inInventory && !_state.SeenInInventoryItemIds.Contains(book.Id, StringComparer.OrdinalIgnoreCase))
                {
                    _state.SeenInInventoryItemIds.Add(book.Id);
                }

                var isCurrentlyInInventory = _state.CurrentInventoryItemIds.Contains(book.Id, StringComparer.OrdinalIgnoreCase);
                var seenInInventory = _state.SeenInInventoryItemIds.Contains(book.Id, StringComparer.OrdinalIgnoreCase);
                var isRead = readBookIds.Contains(book.Id, StringComparer.OrdinalIgnoreCase);

                var level = ResolveBookLevel(book);
                var skillLevel = ResolveSkillLevelForBook(book, skillsMap);
                var (tierMin, tierMax) = ResolveTierRange(level);
                var hasActiveBoostWindow = skillLevel >= tierMin && skillLevel < tierMax;
                var isReadWithActiveBoost = isRead && hasActiveBoostWindow;

                var shouldCheck = isCurrentlyInInventory || seenInInventory || isReadWithActiveBoost || isObsolete;
                book.IsChecked = shouldCheck;
                book.SessionState = isObsolete
                    ? L("No Longer Needed (skill level too high)", "Nicht mehr benötigt (Skill-Stufe zu hoch)")
                    : isCurrentlyInInventory
                        ? L("In Inventory", "Im Inventar")
                        : isReadWithActiveBoost
                            ? L("Read", "Gelesen")
                            : seenInInventory
                                ? L("Seen in Inventory", "Befand sich mal im Inventar")
                                : L("Open", "Noch offen");

                if (shouldCheck)
                {
                    _state.CheckedItems[book.Id] = true;
                }
                else
                {
                    _state.CheckedItems.Remove(book.Id);
                }
            }

            foreach (var magazine in _magazineItems)
            {
                var inInventory = inventoryMagazineIds.Contains(magazine.Id, StringComparer.OrdinalIgnoreCase);
                var isRead = readMagazineIds.Contains(magazine.Id, StringComparer.OrdinalIgnoreCase);
                var shouldCheck = inInventory || isRead;
                magazine.IsChecked = shouldCheck;
                magazine.SessionState = isRead
                    ? L("Read", "Gelesen")
                    : inInventory
                        ? L("In Inventory", "Im Inventar")
                        : L("Open", "Noch offen");

                if (shouldCheck)
                {
                    _state.CheckedItems[magazine.Id] = true;
                }
                else
                {
                    _state.CheckedItems.Remove(magazine.Id);
                }
            }

            foreach (var recipe in _recipeItems)
            {
                var learned = learnedRecipeIds.Contains(recipe.Id, StringComparer.OrdinalIgnoreCase);
                recipe.IsChecked = learned;
                recipe.SessionState = learned ? L("Learned", "Gelernt") : L("Open", "Noch offen");

                if (learned)
                {
                    _state.CheckedItems[recipe.Id] = true;
                }
                else
                {
                    _state.CheckedItems.Remove(recipe.Id);
                }
            }
        }
        finally
        {
            _suppressItemStateWrite = false;
        }

        ApplyFilters();
        UpdateProgress();
    }

    private void ApplySessionSkills(IReadOnlyCollection<SessionSkillLevel> skills)
    {
        SessionSkills.Clear();
        foreach (var skill in skills
                     .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase))
        {
            SessionSkills.Add(new SessionSkillLevelViewModel
            {
                Name = skill.Name,
                Level = skill.Level,
            });
        }

        SessionSkillsHeader = L($"Session Skills ({SessionSkills.Count})", $"Session-Skills ({SessionSkills.Count})");
    }

    private void RebuildItems(GuideCatalog catalog)
    {
        foreach (var existingItem in _allItems)
        {
            existingItem.PropertyChanged -= OnChecklistItemChanged;
        }

        _catalogItems.Clear();
        _allItems.Clear();
        _itemsById.Clear();
        _bookItems.Clear();
        _magazineItems.Clear();
        _recipeItems.Clear();

        foreach (var item in catalog.Items)
        {
            _catalogItems.Add(item);
            if (item.Type == GuideItemType.Profession)
            {
                continue;
            }

            var isChecked = _state.CheckedItems.TryGetValue(item.Id, out var stored) && stored;
            var vmItem = new ChecklistItemViewModel(item, isChecked);
            vmItem.SessionState = string.Empty;
            vmItem.PropertyChanged += OnChecklistItemChanged;
            _allItems.Add(vmItem);
            _itemsById[item.Id] = vmItem;
        }

        _bookItems.AddRange(_allItems.Where(item => item.Type == GuideItemType.Book));
        _magazineItems.AddRange(_allItems.Where(item => item.Type == GuideItemType.Magazine));
        _recipeItems.AddRange(_allItems.Where(item => item.Type == GuideItemType.Recipe));

        ApplyFilters();
        UpdateProgress();
    }

    private void ApplyFilters()
    {
        var filter = SearchText.Trim();
        var bookStatusFilter = SelectedBookStatusFilter?.Key ?? "all";
        var magazineStatusFilter = SelectedMagazineStatusFilter?.Key ?? "all";
        var recipeStatusFilter = SelectedRecipeStatusFilter?.Key ?? "all";

        CaptureBookGroupExpansionStates();
        CaptureMagazineGroupExpansionStates();
        ReplaceBookGroups(FilteredBookGroups, BuildBookGroups(_bookItems, filter, bookStatusFilter));
        ReplaceMagazineGroups(FilteredMagazineGroups, BuildMagazineGroups(_magazineItems, filter, magazineStatusFilter));
        ReplaceCollection(FilteredRecipes, FilterFlat(_recipeItems, filter, recipeStatusFilter));
    }

    private IReadOnlyList<BookCategoryGroupViewModel> BuildBookGroups(
        IEnumerable<ChecklistItemViewModel> books,
        string search,
        string statusFilter)
    {
        var filtered = FilterFlat(books, search, statusFilter);

        return filtered
            .GroupBy(book => string.IsNullOrWhiteSpace(book.Category) ? "Allgemein" : book.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BookCategoryGroupViewModel
            {
                Category = group.Key,
                CategoryGerman = GetLocalizedCategoryName(group.Key),
                CheckedCount = group.Count(item => item.IsChecked),
                TotalCount = group.Count(),
                IsExpanded = !_bookGroupExpandedStates.TryGetValue(group.Key, out var isExpanded) || isExpanded,
                Subtitle = L("Sorted by skill tier (1 to 5)", "Nach Skill-Stufe sortiert (1 bis 5)"),
                Items = new ObservableCollection<ChecklistItemViewModel>(
                    group
                        .OrderBy(item => item.Level <= 0 ? int.MaxValue : item.Level)
                        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)),
            })
            .ToList();
    }

    private IReadOnlyList<BookCategoryGroupViewModel> BuildMagazineGroups(
        IEnumerable<ChecklistItemViewModel> magazines,
        string search,
        string statusFilter)
    {
        var filtered = FilterFlat(magazines, search, statusFilter);

        return filtered
            .GroupBy(magazine => string.IsNullOrWhiteSpace(magazine.Category) ? "Allgemein" : magazine.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new BookCategoryGroupViewModel
            {
                Category = group.Key,
                CategoryGerman = GetLocalizedCategoryName(group.Key),
                CheckedCount = group.Count(item => item.IsChecked),
                TotalCount = group.Count(),
                IsExpanded = !_magazineGroupExpandedStates.TryGetValue(group.Key, out var isExpanded) || isExpanded,
                Subtitle = L("Sorted by magazine category", "Nach Magazin-Typ sortiert"),
                Items = new ObservableCollection<ChecklistItemViewModel>(
                    group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)),
            })
            .ToList();
    }

    private static IReadOnlyList<ChecklistItemViewModel> FilterFlat(
        IEnumerable<ChecklistItemViewModel> source,
        string search,
        string statusFilter)
    {
        return source
            .Where(item => MatchesStatusFilter(item, statusFilter))
            .Where(item =>
                string.IsNullOrWhiteSpace(search) ||
                item.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.GermanName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Detail.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.SessionState.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Source.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesStatusFilter(ChecklistItemViewModel item, string? statusFilter)
    {
        var key = string.IsNullOrWhiteSpace(statusFilter)
            ? "all"
            : statusFilter.ToLowerInvariant();
        var state = item.SessionState ?? string.Empty;
        return key switch
        {
            "all" => true,
            "open" => state.StartsWith("Open", StringComparison.OrdinalIgnoreCase) ||
                      state.StartsWith("Noch offen", StringComparison.OrdinalIgnoreCase),
            "in_inventory" => state.StartsWith("In Inventory", StringComparison.OrdinalIgnoreCase) ||
                              state.StartsWith("Im Inventar", StringComparison.OrdinalIgnoreCase),
            "seen_inventory" => state.StartsWith("Seen in Inventory", StringComparison.OrdinalIgnoreCase) ||
                                state.StartsWith("Befand sich mal im Inventar", StringComparison.OrdinalIgnoreCase),
            "read" => state.StartsWith("Read", StringComparison.OrdinalIgnoreCase) ||
                      state.StartsWith("Gelesen", StringComparison.OrdinalIgnoreCase),
            "obsolete" => state.StartsWith("No Longer Needed", StringComparison.OrdinalIgnoreCase) ||
                          state.StartsWith("Nicht mehr", StringComparison.OrdinalIgnoreCase),
            "learned" => state.StartsWith("Learned", StringComparison.OrdinalIgnoreCase) ||
                         state.StartsWith("Gelernt", StringComparison.OrdinalIgnoreCase),
            "checked" => item.IsChecked,
            "unchecked" => !item.IsChecked,
            _ => true,
        };
    }

    private static void ReplaceCollection(
        ICollection<ChecklistItemViewModel> target,
        IEnumerable<ChecklistItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ReplaceBookGroups(
        ICollection<BookCategoryGroupViewModel> target,
        IEnumerable<BookCategoryGroupViewModel> source)
    {
        foreach (var existing in target)
        {
            existing.PropertyChanged -= OnBookGroupPropertyChanged;
        }

        target.Clear();
        foreach (var group in source)
        {
            target.Add(group);
            group.PropertyChanged += OnBookGroupPropertyChanged;
        }
    }

    private void ReplaceMagazineGroups(
        ICollection<BookCategoryGroupViewModel> target,
        IEnumerable<BookCategoryGroupViewModel> source)
    {
        foreach (var existing in target)
        {
            existing.PropertyChanged -= OnMagazineGroupPropertyChanged;
        }

        target.Clear();
        foreach (var group in source)
        {
            target.Add(group);
            group.PropertyChanged += OnMagazineGroupPropertyChanged;
        }
    }

    private async void OnChecklistItemChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_suppressItemStateWrite ||
            sender is not ChecklistItemViewModel item ||
            eventArgs.PropertyName != nameof(ChecklistItemViewModel.IsChecked))
        {
            return;
        }

        _state.CheckedItems[item.Id] = item.IsChecked;
        if (!item.IsChecked)
        {
            _state.CheckedItems.Remove(item.Id);
        }

        ApplyFilters();
        UpdateProgress();
        await SaveStateAsync();
    }

    private void CaptureBookGroupExpansionStates()
    {
        foreach (var group in FilteredBookGroups)
        {
            _bookGroupExpandedStates[group.Category] = group.IsExpanded;
        }
    }

    private void CaptureMagazineGroupExpansionStates()
    {
        foreach (var group in FilteredMagazineGroups)
        {
            _magazineGroupExpandedStates[group.Category] = group.IsExpanded;
        }
    }

    private void OnBookGroupPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not BookCategoryGroupViewModel group ||
            eventArgs.PropertyName != nameof(BookCategoryGroupViewModel.IsExpanded))
        {
            return;
        }

        _bookGroupExpandedStates[group.Category] = group.IsExpanded;
    }

    private void OnMagazineGroupPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (sender is not BookCategoryGroupViewModel group ||
            eventArgs.PropertyName != nameof(BookCategoryGroupViewModel.IsExpanded))
        {
            return;
        }

        _magazineGroupExpandedStates[group.Category] = group.IsExpanded;
    }

    private void UpdateProgress()
    {
        BookProgress = BuildProgressText(_bookItems);
        MagazineProgress = BuildProgressText(_magazineItems);
        RecipeProgress = BuildProgressText(_recipeItems);
    }

    private static string BuildProgressText(IReadOnlyCollection<ChecklistItemViewModel> items)
    {
        if (items.Count == 0)
        {
            return "0 / 0";
        }

        var checkedCount = items.Count(item => item.IsChecked);
        return $"{checkedCount} / {items.Count}";
    }

    private async void SessionTimerOnTick(object? sender, EventArgs eventArgs)
    {
        if (!AutoSessionSync || IsBusy)
        {
            return;
        }

        ConfigureSessionWatcher();
        await SyncSessionAsync(isManual: false);
    }

    private async void SessionWatcherDebounceOnTick(object? sender, EventArgs eventArgs)
    {
        _sessionWatcherDebounceTimer.Stop();
        if (!_sessionWatcherDirty || !AutoSessionSync || IsBusy)
        {
            _sessionWatcherDirty = false;
            return;
        }

        _sessionWatcherDirty = false;
        await SyncSessionAsync(isManual: false);
    }

    private void ConfigureSessionWatcher()
    {
        var savePath = _sessionSyncService.TryResolveActiveSavePathForCurrentSession();
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath))
        {
            DisposeSessionWatcher();
            return;
        }

        if (string.Equals(_watchedSavePath, savePath, StringComparison.OrdinalIgnoreCase) &&
            _sessionWatcher is not null)
        {
            return;
        }

        DisposeSessionWatcher();
        _watchedSavePath = savePath;
        _sessionWatcher = new FileSystemWatcher(savePath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };

        _sessionWatcher.Changed += OnSessionSaveFileChanged;
        _sessionWatcher.Created += OnSessionSaveFileChanged;
        _sessionWatcher.Deleted += OnSessionSaveFileChanged;
        _sessionWatcher.Renamed += OnSessionSaveFileRenamed;
    }

    private void OnSessionSaveFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        if (!IsPlayersDbFile(eventArgs.Name))
        {
            return;
        }

        _sessionWatcherDirty = true;
        _sessionWatcherDebounceTimer.Stop();
        _sessionWatcherDebounceTimer.Start();
    }

    private void OnSessionSaveFileRenamed(object sender, RenamedEventArgs eventArgs)
    {
        if (!IsPlayersDbFile(eventArgs.Name) && !IsPlayersDbFile(eventArgs.OldName))
        {
            return;
        }

        _sessionWatcherDirty = true;
        _sessionWatcherDebounceTimer.Stop();
        _sessionWatcherDebounceTimer.Start();
    }

    private static bool IsPlayersDbFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return fileName.StartsWith("players.db", StringComparison.OrdinalIgnoreCase);
    }

    private void DisposeSessionWatcher()
    {
        _watchedSavePath = string.Empty;
        if (_sessionWatcher is null)
        {
            return;
        }

        _sessionWatcher.EnableRaisingEvents = false;
        _sessionWatcher.Changed -= OnSessionSaveFileChanged;
        _sessionWatcher.Created -= OnSessionSaveFileChanged;
        _sessionWatcher.Deleted -= OnSessionSaveFileChanged;
        _sessionWatcher.Renamed -= OnSessionSaveFileRenamed;
        _sessionWatcher.Dispose();
        _sessionWatcher = null;
    }

    private async Task ReloadAndResyncForLanguageChangeAsync()
    {
        await ReloadDataAsync(preferGameFiles: !string.IsNullOrWhiteSpace(GamePath));
        if (AutoSessionSync)
        {
            await SyncSessionAsync(isManual: false);
        }
    }

    private async Task RefreshAvailableLanguagesAsync()
    {
        var languages = await Task.Run(() => _guideCatalogService.GetAvailableLanguageCodes(GamePath, IncludeMods));
        var options = languages
            .Select(code => new LanguageOptionViewModel
            {
                Code = code,
                Name = GetLanguageDisplayName(code),
            })
            .ToList();

        if (options.Count == 0)
        {
            options.Add(new LanguageOptionViewModel
            {
                Code = "EN",
                Name = GetLanguageDisplayName("EN"),
            });
        }

        AvailableLanguages.Clear();
        foreach (var option in options)
        {
            AvailableLanguages.Add(option);
        }

        var selectedCode = string.IsNullOrWhiteSpace(_state.LanguageCode)
            ? "EN"
            : _state.LanguageCode.ToUpperInvariant();
        var selectedOption = AvailableLanguages.FirstOrDefault(option =>
            option.Code.Equals(selectedCode, StringComparison.OrdinalIgnoreCase)) ?? AvailableLanguages.First();

        _suppressLanguageReload = true;
        try
        {
            SelectedLanguage = selectedOption;
        }
        finally
        {
            _suppressLanguageReload = false;
        }
    }

    private async Task TrySyncSessionOnStartupAsync()
    {
        var savePath = _sessionSyncService.TryResolveActiveSavePathForCurrentSession();
        if (string.IsNullOrWhiteSpace(savePath) || !Directory.Exists(savePath))
        {
            return;
        }

        var playersDbPath = Path.Combine(savePath, "players.db");
        if (!File.Exists(playersDbPath))
        {
            return;
        }

        await SyncSessionAsync(isManual: false);
    }

    private void RefreshStatusFilters()
    {
        var selectedBookKey = string.IsNullOrWhiteSpace(_state.BookStatusFilterKey) ? "all" : _state.BookStatusFilterKey;
        var selectedMagazineKey = string.IsNullOrWhiteSpace(_state.MagazineStatusFilterKey) ? "all" : _state.MagazineStatusFilterKey;
        var selectedRecipeKey = string.IsNullOrWhiteSpace(_state.RecipeStatusFilterKey) ? "all" : _state.RecipeStatusFilterKey;

        _suppressStatusFilterReload = true;
        try
        {
            ReplaceStatusOptions(
                AvailableBookStatusFilters,
                CreateStatusFilterOptions(
                    ("all", L("All", "Alle")),
                    ("open", L("Open", "Offen")),
                    ("in_inventory", L("In Inventory", "Im Inventar")),
                    ("seen_inventory", L("Seen In Inventory", "War im Inventar")),
                    ("read", L("Read", "Gelesen")),
                    ("obsolete", L("No Longer Needed", "Nicht mehr benoetigt")),
                    ("checked", L("Checked", "Abgehakt")),
                    ("unchecked", L("Unchecked", "Nicht abgehakt"))));

            ReplaceStatusOptions(
                AvailableMagazineStatusFilters,
                CreateStatusFilterOptions(
                    ("all", L("All", "Alle")),
                    ("open", L("Open", "Offen")),
                    ("in_inventory", L("In Inventory", "Im Inventar")),
                    ("read", L("Read", "Gelesen")),
                    ("checked", L("Checked", "Abgehakt")),
                    ("unchecked", L("Unchecked", "Nicht abgehakt"))));

            ReplaceStatusOptions(
                AvailableRecipeStatusFilters,
                CreateStatusFilterOptions(
                    ("all", L("All", "Alle")),
                    ("open", L("Open", "Offen")),
                    ("learned", L("Learned", "Gelernt")),
                    ("checked", L("Checked", "Abgehakt")),
                    ("unchecked", L("Unchecked", "Nicht abgehakt"))));

            SelectedBookStatusFilter = ResolveStatusFilterSelection(AvailableBookStatusFilters, selectedBookKey);
            SelectedMagazineStatusFilter = ResolveStatusFilterSelection(AvailableMagazineStatusFilters, selectedMagazineKey);
            SelectedRecipeStatusFilter = ResolveStatusFilterSelection(AvailableRecipeStatusFilters, selectedRecipeKey);
        }
        finally
        {
            _suppressStatusFilterReload = false;
        }

        _state.BookStatusFilterKey = SelectedBookStatusFilter?.Key ?? "all";
        _state.MagazineStatusFilterKey = SelectedMagazineStatusFilter?.Key ?? "all";
        _state.RecipeStatusFilterKey = SelectedRecipeStatusFilter?.Key ?? "all";
    }

    private void ApplyUiLanguage()
    {
        RefreshStatusFilters();
        SessionSkillsHeader = L(
            $"Session Skills ({SessionSkills.Count})",
            $"Session-Skills ({SessionSkills.Count})");

        LastSyncText = _state.LastSyncAt.HasValue
            ? L($"Last catalog sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}", $"Letzte Katalog-Sync: {_state.LastSyncAt:dd.MM.yyyy HH:mm}")
            : L("No catalog sync yet", "Noch keine Katalog-Synchronisierung");

        LastSessionSyncText = _state.LastSessionSyncAt.HasValue
            ? L($"Last session sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}", $"Letzte Session-Sync: {_state.LastSessionSyncAt:dd.MM.yyyy HH:mm}")
            : L("No session sync yet", "Noch keine Session-Synchronisierung");

        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(GamePathWatermarkText));
        OnPropertyChanged(nameof(ModsOffText));
        OnPropertyChanged(nameof(ModsOnText));
        OnPropertyChanged(nameof(AutoDetectPathButtonText));
        OnPropertyChanged(nameof(LoadFromGameButtonText));
        OnPropertyChanged(nameof(LoadFallbackButtonText));
        OnPropertyChanged(nameof(SyncSessionButtonText));
        OnPropertyChanged(nameof(SearchWatermarkText));
        OnPropertyChanged(nameof(AutoSessionSyncOffText));
        OnPropertyChanged(nameof(AutoSessionSyncOnText));
        OnPropertyChanged(nameof(ClearChecksButtonText));
        OnPropertyChanged(nameof(UpdateFeedPathWatermarkText));
        OnPropertyChanged(nameof(AutoUpdateOffText));
        OnPropertyChanged(nameof(AutoUpdateOnText));
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
        OnPropertyChanged(nameof(InstallUpdateButtonText));
        OnPropertyChanged(nameof(LanguageLabelText));
        OnPropertyChanged(nameof(BookFilterLabelText));
        OnPropertyChanged(nameof(MagazineFilterLabelText));
        OnPropertyChanged(nameof(RecipeFilterLabelText));
        OnPropertyChanged(nameof(TwitchButtonText));
        OnPropertyChanged(nameof(BooksTabHeader));
        OnPropertyChanged(nameof(MagazinesTabHeader));
        OnPropertyChanged(nameof(RecipesTabHeader));
    }

    private static StatusFilterOptionViewModel ResolveStatusFilterSelection(
        IEnumerable<StatusFilterOptionViewModel> options,
        string key)
    {
        return options.FirstOrDefault(option => option.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? options.First();
    }

    private static IReadOnlyList<StatusFilterOptionViewModel> CreateStatusFilterOptions(
        params (string Key, string Label)[] options)
    {
        return options
            .Select(option => new StatusFilterOptionViewModel
            {
                Key = option.Key,
                Label = option.Label,
            })
            .ToList();
    }

    private static void ReplaceStatusOptions(
        ICollection<StatusFilterOptionViewModel> target,
        IEnumerable<StatusFilterOptionViewModel> source)
    {
        target.Clear();
        foreach (var option in source)
        {
            target.Add(option);
        }
    }

    private string L(string english, string german)
    {
        return IsGermanUi ? german : english;
    }

    private bool IsGermanUi => (_state.LanguageCode ?? string.Empty)
        .Equals("DE", StringComparison.OrdinalIgnoreCase);

    private static string GetLanguageDisplayName(string languageCode)
    {
        var normalized = languageCode.ToUpperInvariant();
        return normalized switch
        {
            "EN" => "English",
            "DE" => "Deutsch",
            "AR" => "العربية",
            "CA" => "Català",
            "CH" => "繁體中文",
            "CN" => "简体中文",
            "CS" => "Čeština",
            "DA" => "Dansk",
            "ES" => "Español",
            "FI" => "Suomi",
            "FR" => "Français",
            "HU" => "Magyar",
            "ID" => "Bahasa Indonesia",
            "IT" => "Italiano",
            "JP" => "日本語",
            "KO" => "한국어",
            "NL" => "Nederlands",
            "NO" => "Norsk",
            "PH" => "Filipino",
            "PL" => "Polski",
            "PT" => "Português",
            "PTBR" => "Português (Brasil)",
            "RO" => "Română",
            "RU" => "Русский",
            "TH" => "ไทย",
            "TR" => "Türkçe",
            "UA" => "Українська",
            _ => normalized,
        };
    }

    private async Task SaveStateAsync()
    {
        try
        {
            _state.LanguageCode = string.IsNullOrWhiteSpace(_state.LanguageCode)
                ? "EN"
                : _state.LanguageCode.ToUpperInvariant();
            _state.BookStatusFilterKey = string.IsNullOrWhiteSpace(_state.BookStatusFilterKey)
                ? "all"
                : _state.BookStatusFilterKey.ToLowerInvariant();
            _state.MagazineStatusFilterKey = string.IsNullOrWhiteSpace(_state.MagazineStatusFilterKey)
                ? "all"
                : _state.MagazineStatusFilterKey.ToLowerInvariant();
            _state.RecipeStatusFilterKey = string.IsNullOrWhiteSpace(_state.RecipeStatusFilterKey)
                ? "all"
                : _state.RecipeStatusFilterKey.ToLowerInvariant();
            _state.SeenInInventoryItemIds = _state.SeenInInventoryItemIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _state.CurrentInventoryItemIds = _state.CurrentInventoryItemIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            await _appStateService.SaveAsync(_state);
        }
        catch
        {
            // Persist errors should not crash the UI.
        }
    }

    private static Version GetCurrentAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var clean = informational.Split('+')[0];
            var prereleaseIndex = clean.IndexOf('-');
            if (prereleaseIndex >= 0)
            {
                clean = clean[..prereleaseIndex];
            }
            if (Version.TryParse(clean, out var parsedInformational))
            {
                return parsedInformational;
            }
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            return new Version(assemblyVersion.Major, assemblyVersion.Minor, Math.Max(assemblyVersion.Build, 0));
        }

        return new Version(1, 0, 0);
    }

    private string GetLocalizedCategoryName(string category)
    {
        if (!IsGermanUi)
        {
            var normalizedKey = NormalizeSkillKey(category);
            return normalizedKey switch
            {
                "allgemein" => "General",
                "rezepte" => "Recipes",
                _ => category,
            };
        }

        var germanKey = NormalizeSkillKey(category);
        return germanKey switch
        {
            "carpentry" or "woodwork" => "Tischlerei",
            "carving" => "Schnitzen",
            "cooking" => "Kochen",
            "electrical" or "electricity" or "electrician" => "Elektrotechnik",
            "farming" => "Landwirtschaft",
            "firstaid" or "doctor" => "Erste Hilfe",
            "fishing" => "Angeln",
            "flintknapping" or "knapping" => "Feuersteinschlagen",
            "glassmaking" => "Glasherstellung",
            "foraging" => "Nahrungssuche",
            "masonry" => "Mauerwerk",
            "mechanics" or "mechanic" => "Kfz-Mechanik",
            "metalworking" or "metalwelding" or "metalwork" => "Metallbearbeitung",
            "blacksmith" or "blacksmithing" or "smithing" => "Schmieden",
            "maintenance" => "Instandhaltung",
            "pottery" => "Töpferei",
            "tailoring" => "Schneiderei",
            "trapping" => "Fallenstellen",
            "combat" => "Kampf",
            "magazine" or "magazines" => "Magazine",
            "allgemein" => "Allgemein",
            _ => category,
        };
    }

    private static int ResolveBookLevel(ChecklistItemViewModel book)
    {
        if (book.Level > 0)
        {
            return book.Level;
        }

        var digits = new string(book.Detail.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var level) ? level : 0;
    }

    private static string NormalizeSkillKey(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static int ResolveSkillLevelForBook(ChecklistItemViewModel book, IReadOnlyDictionary<string, int> skillsMap)
    {
        if (string.IsNullOrWhiteSpace(book.Category))
        {
            return 0;
        }

        var raw = NormalizeSkillKey(book.Category);
        var mapped = raw switch
        {
            "woodwork" => "carpentry",
            "electricity" => "electrical",
            "electrician" => "electrical",
            "doctor" => "firstaid",
            "knapping" => "flintknapping",
            "metalwelding" => "metalworking",
            "metalwork" => "metalworking",
            "blacksmithing" => "blacksmith",
            "smithing" => "blacksmith",
            _ => raw,
        };

        return skillsMap.TryGetValue(mapped, out var level) ? level : 0;
    }

    private static (int Min, int Max) ResolveTierRange(int bookLevel)
    {
        if (bookLevel <= 0)
        {
            return (0, 0);
        }

        var min = (bookLevel - 1) * 2;
        var max = bookLevel * 2;
        return (min, max);
    }
}
