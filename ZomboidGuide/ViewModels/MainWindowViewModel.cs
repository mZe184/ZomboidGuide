using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZomboidGuide.Models;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const int CurrentInventoryDetectionVersion = 5;
    private const string DefaultGitHubUpdateRepository = "https://github.com/mZe184/ZomboidGuide";
    private static readonly IBrush RiskUnknownBrush = Brush.Parse("#4C5840");
    private static readonly IBrush RiskSafeBrush = Brush.Parse("#2F5A3E");
    private static readonly IBrush RiskRiskyBrush = Brush.Parse("#7A5A2E");
    private static readonly IBrush RiskCriticalBrush = Brush.Parse("#7A2E2E");

    private readonly AppStateService _appStateService = new();
    private readonly GuideCatalogService _guideCatalogService = new();
    private readonly SessionSyncService _sessionSyncService = new();
    private readonly AppUpdateService _appUpdateService = new();
    private readonly UiLocalizationService _uiLocalizationService = new();

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
    private readonly Dictionary<string, TodoTaskViewModel> _todoItemsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TodoTaskViewModel> _todoLeafItems = [];

    private AppState _state = new();
    private UpdateCheckResult? _latestUpdateResult;
    private bool _suppressItemStateWrite;
    private bool _sessionSyncRunning;
    private bool _suppressLanguageReload;
    private bool _suppressStatusFilterReload;
    private bool _suppressTodoStateWrite;
    private bool _isInitializing;
    private bool _skipUpdatePromptForSession;
    private FileSystemWatcher? _sessionWatcher;
    private bool _sessionWatcherDirty;
    private string _watchedSavePath = string.Empty;
    private DateTime _lastObservedPlayersDbWriteUtc = DateTime.MinValue;

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
    private bool riskIndicatorEnabled;

    [ObservableProperty]
    private SessionRiskLevel riskLevel = SessionRiskLevel.Unknown;

    [ObservableProperty]
    private int riskScore;

    [ObservableProperty]
    private string riskNotes = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string updateStatusMessage = string.Empty;

    [ObservableProperty]
    private string releaseVersionText = "Version: -";

    [ObservableProperty]
    private bool isUpdateAvailable;

    [ObservableProperty]
    private bool isUpdatePromptVisible;

    [ObservableProperty]
    private string dataSource = string.Empty;

    [ObservableProperty]
    private string lastSyncText = string.Empty;

    [ObservableProperty]
    private string lastSessionSyncText = string.Empty;

    [ObservableProperty]
    private string bookProgress = "0 / 0";

    [ObservableProperty]
    private string magazineProgress = "0 / 0";

    [ObservableProperty]
    private string recipeProgress = "0 / 0";

    [ObservableProperty]
    private ObservableCollection<TodoTaskViewModel> todoItems = [];

    [ObservableProperty]
    private string todoProgress = "0 / 0";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isDiagnosticsVisible;

    [ObservableProperty]
    private string diagnosticsText = string.Empty;

    public string WindowTitleText => L("MietzeMatze's Zomboid Guide", "MietzeMatze's Zomboid Guide");

    public string HeaderTitleText => L("MietzeMatze's Zomboid Guide", "MietzeMatze's Zomboid Guide");

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

    public string DiagnosticsButtonText => L("Show Diagnostics", "Diagnose anzeigen");

    public string DiagnosticsDialogTitleText => L("Diagnostics", "Diagnose");

    public string CloseDiagnosticsButtonText => L("Close", "Schließen");

    public string SearchWatermarkText => L(
        "Search skills, books, magazines, recipes ...",
        "Suche in Skills, Büchern, Magazinen, Rezepten ...");

    public string AutoSessionSyncOffText => L("Auto Session Sync Off", "Auto-Session-Sync aus");

    public string AutoSessionSyncOnText => L("Auto Session Sync On", "Auto-Session-Sync an");

    public string ClearChecksButtonText => L("Clear All Checks", "Alle Haken entfernen");

    public string AutoUpdateOffText => L("Auto Update Check Off", "Auto-Updatecheck aus");

    public string AutoUpdateOnText => L("Auto Update Check On", "Auto-Updatecheck an");

    public string RiskIndicatorOffText => L("Risk Indicator Off", "Risiko-Indikator aus");

    public string RiskIndicatorOnText => L("Risk Indicator On", "Risiko-Indikator an");

    public string RiskIndicatorTitleText => L("Survival Risk", "Überlebensrisiko");

    public bool IsRiskIndicatorVisible => RiskIndicatorEnabled;

    public string RiskLevelText => RiskLevel switch
    {
        SessionRiskLevel.Safe => L("Safe", "Sicher"),
        SessionRiskLevel.Risky => L("Risky", "Riskant"),
        SessionRiskLevel.Critical => L("Critical", "Kritisch"),
        _ => L("Unknown", "Unbekannt"),
    };

    public string RiskScoreText => Lf("Risk score: {0}/100", "Risiko-Score: {0}/100", RiskScore);

    public string RiskHintText
    {
        get
        {
            var guidance = RiskLevel switch
            {
                SessionRiskLevel.Safe => L("Stable for now. Keep food, water, and rest up.", "Aktuell stabil. Nahrung, Wasser und Ruhe beibehalten."),
                SessionRiskLevel.Risky => L("Warning: eat, drink, and rest soon.", "Warnung: bald essen, trinken und ausruhen."),
                SessionRiskLevel.Critical => L("Critical: sleep, eat, and treat wounds now.", "Kritisch: jetzt schlafen, essen und Wunden behandeln."),
                _ => L("No fresh session risk data yet.", "Noch keine frischen Session-Risikodaten."),
            };

            return string.IsNullOrWhiteSpace(RiskNotes)
                ? guidance
                : $"{guidance}{Environment.NewLine}{RiskNotes}";
        }
    }

    public IBrush RiskBadgeBrush => RiskLevel switch
    {
        SessionRiskLevel.Safe => RiskSafeBrush,
        SessionRiskLevel.Risky => RiskRiskyBrush,
        SessionRiskLevel.Critical => RiskCriticalBrush,
        _ => RiskUnknownBrush,
    };

    public string CheckUpdatesButtonText => L("Check For Update", "Nach Update suchen");

    public string InstallUpdateButtonText => L("Install Update", "Update installieren");

    public string DismissUpdatePromptButtonText => L("Skip For This Session", "Für diese Session überspringen");

    public string UpdatePromptTitleText => L("Update available", "Update verfügbar");

    public string UpdatePromptMessageText
    {
        get
        {
            if (_latestUpdateResult?.AvailableVersion is not null)
            {
                return Lf(
                    "A newer version ({0}) is available. Install now?",
                    "Eine neuere Version ({0}) ist verfügbar. Jetzt installieren?",
                    _latestUpdateResult.AvailableVersion);
            }

            return L(
                "A newer version is available. Install now?",
                "Eine neuere Version ist verfügbar. Jetzt installieren?");
        }
    }

    public string ReleaseVersionLabelText => L("Version", "Version");

    public string BooksTabHeader => $"{L("Books", "Bücher")} ({BookProgress})";

    public string MagazinesTabHeader => $"{L("Magazines", "Magazine")} ({MagazineProgress})";

    public string RecipesTabHeader => $"{L("Recipes", "Rezepte")} ({RecipeProgress})";

    public string TodoTabHeader => $"{L("ToDo", "ToDo")} ({TodoProgress})";

    public string TodoSubtitleText => L(
        "Recommended run flow. Manual checks are possible; many steps complete automatically from your books, magazines, recipes, and session skills.",
        "Empfohlener Run-Ablauf. Manuelle Haken sind möglich; viele Schritte werden automatisch aus Büchern, Magazinen, Rezepten und Session-Skills abgeschlossen.");

    public string CopyrightLabelText => "Copyright (c)";

    public string TwitchButtonText => L("MietzeMatze on Twitch", "MietzeMatze auf Twitch");

    public string TwitchButtonTextZickchen => L("for Zickchen69 on Twitch", "für Zickchen69 auf Twitch");

    public string LanguageLabelText => L("Language", "Sprache");

    public string BookFilterLabelText => L("Books Filter", "Bücher-Filter");

    public string MagazineFilterLabelText => L("Magazines Filter", "Magazine-Filter");

    public string RecipeFilterLabelText => L("Recipes Filter", "Rezepte-Filter");

    public MainWindowViewModel()
    {
        ApplyInitialUiTextDefaults();
        UpdateSessionPollingInterval();
        _sessionTimer.Tick += SessionTimerOnTick;
        _sessionWatcherDebounceTimer.Tick += SessionWatcherDebounceOnTick;
        _sessionTimer.Start();
        _ = InitializeAsync();
    }

    private void ApplyInitialUiTextDefaults()
    {
        StatusMessage = L("Loading data ...", "Lade Daten ...");
        UpdateStatusMessage = L("Update: not checked yet", "Update: noch nicht geprüft");
        DataSource = L("Not loaded yet", "Noch nicht geladen");
        LastSyncText = L("No catalog sync yet", "Noch keine Katalog-Synchronisierung");
        LastSessionSyncText = L("No session sync yet", "Noch keine Session-Synchronisierung");
        RiskLevel = SessionRiskLevel.Unknown;
        RiskScore = 0;
        RiskNotes = string.Empty;
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

        var languageCode = NormalizeLanguageCode(value?.Code);
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

    partial void OnRiskIndicatorEnabledChanged(bool value)
    {
        _state.RiskIndicatorEnabled = value;
        UpdateSessionPollingInterval();
        OnPropertyChanged(nameof(IsRiskIndicatorVisible));

        if (_isInitializing)
        {
            return;
        }

        if (!value)
        {
            ResetRiskIndicator();
        }

        _ = SaveStateAsync();

        if (value)
        {
            ConfigureSessionWatcher();
            _ = SyncSessionAsync(isManual: false);
        }
    }

    partial void OnRiskLevelChanged(SessionRiskLevel value)
    {
        OnPropertyChanged(nameof(RiskLevelText));
        OnPropertyChanged(nameof(RiskHintText));
        OnPropertyChanged(nameof(RiskBadgeBrush));
    }

    partial void OnRiskScoreChanged(int value)
    {
        OnPropertyChanged(nameof(RiskScoreText));
    }

    partial void OnRiskNotesChanged(string value)
    {
        OnPropertyChanged(nameof(RiskHintText));
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

    partial void OnTodoProgressChanged(string value)
    {
        OnPropertyChanged(nameof(TodoTabHeader));
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
    private void ShowDiagnostics()
    {
        var currentPathState = string.IsNullOrWhiteSpace(GamePath)
            ? "gamePath=empty"
            : $"gamePath={GamePath}; exists={Directory.Exists(GamePath)}; mediaExists={Directory.Exists(Path.Combine(GamePath, "media"))}";
        var autoDetectDiagnostics = _guideCatalogService.GetAutoDetectGamePathDiagnostics();
        var activeSaveDiagnostics = _sessionSyncService.BuildActiveSaveDiagnostics();

        DiagnosticsText = string.Join(
            Environment.NewLine + Environment.NewLine,
            [
                $"time={DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}",
                FormatDiagnosticsSection("gamePath", currentPathState),
                FormatDiagnosticsSection("autoDetect", autoDetectDiagnostics),
                FormatDiagnosticsSection("activeSave", activeSaveDiagnostics),
            ]);

        IsDiagnosticsVisible = true;
        StatusMessage = L("Diagnostics prepared.", "Diagnose erstellt.");
    }

    [RelayCommand]
    private void CloseDiagnostics()
    {
        IsDiagnosticsVisible = false;
    }

    private static string FormatDiagnosticsSection(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{label}:{Environment.NewLine}(empty)";
        }

        var normalized = value
            .Replace("\r\n", "\n")
            .Replace(" | ", "\n")
            .Replace("; ", "\n")
            .Replace("|", "\n")
            .Replace(";", "\n");

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return lines.Length == 0
            ? $"{label}:{Environment.NewLine}(empty)"
            : $"{label}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

    [RelayCommand]
    private async Task AutoDetectPathAsync()
    {
        var detectedPath = _guideCatalogService.TryAutoDetectGamePath();
        if (string.IsNullOrWhiteSpace(detectedPath))
        {
            var diagnostics = TruncateStatusDetail(_guideCatalogService.GetAutoDetectGamePathDiagnostics());
            StatusMessage = Lf(
                "Could not auto-detect a Project Zomboid installation. Diagnostics: {0}",
                "Keine Project-Zomboid-Installation automatisch gefunden. Diagnose: {0}",
                diagnostics);
            return;
        }

        GamePath = detectedPath;
        StatusMessage = Lf("Path detected: {0}", "Pfad erkannt: {0}", detectedPath);
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
            StatusMessage = Lf("Could not open link: {0}", "Konnte Link nicht öffnen: {0}", exception.Message);
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        await CheckUpdatesCoreAsync(showPromptForThisSession: false);
    }

    private async Task CheckUpdatesCoreAsync(bool showPromptForThisSession)
    {
        var currentVersion = GetCurrentAppVersion();
        var result = await Task.Run(() => _appUpdateService.CheckForUpdate(DefaultGitHubUpdateRepository, currentVersion));
        _state.LastUpdateCheckAt = DateTimeOffset.Now;

        if (!result.Success)
        {
            IsUpdateAvailable = false;
            IsUpdatePromptVisible = false;
            _latestUpdateResult = null;
            UpdateStatusMessage = L("Update check failed.", "Update-Prüfung fehlgeschlagen.");
            UpdateReleaseVersionText();
            OnPropertyChanged(nameof(UpdatePromptMessageText));
            await SaveStateAsync();
            return;
        }

        _latestUpdateResult = result;
        IsUpdateAvailable = result.UpdateAvailable;
        if (result.AvailableVersion is not null)
        {
            _state.LastKnownReleaseVersion = result.AvailableVersion.ToString();
        }

        if (result.UpdateAvailable && result.AvailableVersion is not null)
        {
            UpdateStatusMessage = Lf(
                "Update available. Version: {0}",
                "Update verfügbar. Version: {0}",
                result.AvailableVersion);

            if (showPromptForThisSession && !_skipUpdatePromptForSession)
            {
                IsUpdatePromptVisible = true;
            }
        }
        else
        {
            var release = result.AvailableVersion?.ToString() ?? _state.LastKnownReleaseVersion;
            UpdateStatusMessage = Lf("Version: {0}", "Version: {0}", string.IsNullOrWhiteSpace(release) ? "-" : release);
            IsUpdatePromptVisible = false;
        }

        OnPropertyChanged(nameof(UpdatePromptMessageText));
        UpdateReleaseVersionText();
        await SaveStateAsync();
    }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (_latestUpdateResult is null || !_latestUpdateResult.UpdateAvailable)
        {
            UpdateStatusMessage = L("No update available to install.", "Kein Update zum Installieren gefunden.");
            return;
        }

        IsUpdatePromptVisible = false;

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
    private void DismissUpdatePromptForSession()
    {
        _skipUpdatePromptForSession = true;
        IsUpdatePromptVisible = false;
        UpdateStatusMessage = L(
            "Update reminder dismissed for this session.",
            "Update-Hinweis für diese Session ausgeblendet.");
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
        RefreshTodoAutoStates();
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
            _state.TodoManualChecks ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
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
            RiskIndicatorEnabled = _state.RiskIndicatorEnabled;
            GamePath = _state.GamePath ?? string.Empty;
            _state.LanguageCode = ResolvePreferredLanguageCode(
                _state.LanguageCode,
                _uiLocalizationService.GetSupportedLanguageCodes());
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
            UpdateReleaseVersionText();

            LastSyncText = _state.LastSyncAt.HasValue
                ? Lf("Last catalog sync: {0:dd.MM.yyyy HH:mm}", "Letzte Katalog-Sync: {0:dd.MM.yyyy HH:mm}", _state.LastSyncAt)
                : L("No catalog sync yet", "Noch keine Katalog-Synchronisierung");

            LastSessionSyncText = _state.LastSessionSyncAt.HasValue
                ? Lf("Last session sync: {0:dd.MM.yyyy HH:mm}", "Letzte Session-Sync: {0:dd.MM.yyyy HH:mm}", _state.LastSessionSyncAt)
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
            await CheckUpdatesCoreAsync(showPromptForThisSession: true);
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
            LastSyncText = Lf(
                "Last catalog sync: {0:dd.MM.yyyy HH:mm}",
                "Letzte Katalog-Sync: {0:dd.MM.yyyy HH:mm}",
                _state.LastSyncAt);

            await SaveStateAsync();
            StatusMessage = hadKnownCatalog && newCatalogIds.Count > 0
                ? Lf("Catalog loaded: {0} entries ({1} new)",
                    "Katalog geladen: {0} Einträge ({1} neu)",
                    _allItems.Count, newCatalogIds.Count)
                : Lf("Catalog loaded: {0} entries",
                    "Katalog geladen: {0} Einträge",
                    _allItems.Count);
        }
        catch (Exception exception)
        {
            StatusMessage = Lf("Failed to load data: {0}", "Fehler beim Laden: {0}", exception.Message);
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

            var result = await Task.Run(() => _sessionSyncService.SyncFromCurrentSession(_catalogItems, includeRiskAssessment: RiskIndicatorEnabled));
            if (!result.Success)
            {
                if (RiskIndicatorEnabled)
                {
                    RiskLevel = SessionRiskLevel.Unknown;
                    RiskScore = 0;
                    RiskNotes = L("No fresh risk data available.", "Keine frischen Risikodaten verfügbar.");
                }

                if (isManual)
                {
                    StatusMessage = Lf(
                        "Session sync failed: {0}",
                        "Session-Sync fehlgeschlagen: {0}",
                        TruncateStatusDetail(result.Message));
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
            RefreshTodoAutoStates();
            _lastObservedPlayersDbWriteUtc = ResolvePlayersDbLastWriteUtc(result.SavePath);

            _state.LastSessionSyncAt = DateTimeOffset.Now;
            LastSessionSyncText = Lf(
                "Last session sync: {0:dd.MM.yyyy HH:mm}",
                "Letzte Session-Sync: {0:dd.MM.yyyy HH:mm}",
                _state.LastSessionSyncAt);
            await SaveStateAsync();

            if (RiskIndicatorEnabled)
            {
                ApplyRiskIndicator(result);
            }
            else
            {
                ResetRiskIndicator();
            }

            StatusMessage = L(
                $"Session synced successfully ({result.PlayerName})",
                $"Session erfolgreich synchronisiert ({result.PlayerName})");
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
                book.SessionStateKey = isObsolete
                    ? "obsolete"
                    : isCurrentlyInInventory
                        ? "in_inventory"
                        : isReadWithActiveBoost
                            ? "read"
                            : seenInInventory
                                ? "seen_inventory"
                                : "open";
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
                magazine.SessionStateKey = isRead
                    ? "read"
                    : inInventory
                        ? "in_inventory"
                        : "open";
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
                recipe.SessionStateKey = learned ? "learned" : "open";
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

        SessionSkillsHeader = Lf("Session Skills ({0})", "Session-Skills ({0})", SessionSkills.Count);
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
            vmItem.SessionStateKey = "open";
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
        RebuildTodoPlan();
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
        var stateKey = ResolveSessionStateKey(item);
        return key switch
        {
            "all" => true,
            "open" => string.IsNullOrWhiteSpace(stateKey) || stateKey == "open",
            "in_inventory" => stateKey == "in_inventory",
            "seen_inventory" => stateKey == "seen_inventory",
            "read" => stateKey == "read",
            "obsolete" => stateKey == "obsolete",
            "learned" => stateKey == "learned",
            "checked" => item.IsChecked,
            "unchecked" => !item.IsChecked,
            _ => true,
        };
    }

    private static string ResolveSessionStateKey(ChecklistItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.SessionStateKey))
        {
            return item.SessionStateKey.Trim().ToLowerInvariant();
        }

        var state = item.SessionState ?? string.Empty;
        if (state.StartsWith("In Inventory", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Im Inventar", StringComparison.OrdinalIgnoreCase))
        {
            return "in_inventory";
        }

        if (state.StartsWith("Seen in Inventory", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Befand sich mal im Inventar", StringComparison.OrdinalIgnoreCase))
        {
            return "seen_inventory";
        }

        if (state.StartsWith("No Longer Needed", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Nicht mehr", StringComparison.OrdinalIgnoreCase))
        {
            return "obsolete";
        }

        if (state.StartsWith("Read", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Gelesen", StringComparison.OrdinalIgnoreCase))
        {
            return "read";
        }

        if (state.StartsWith("Learned", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Gelernt", StringComparison.OrdinalIgnoreCase))
        {
            return "learned";
        }

        if (state.StartsWith("Open", StringComparison.OrdinalIgnoreCase) ||
            state.StartsWith("Noch offen", StringComparison.OrdinalIgnoreCase))
        {
            return "open";
        }

        return string.Empty;
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
        RefreshTodoAutoStates();
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
        UpdateTodoProgress();
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

    private void RebuildTodoPlan()
    {
        _state.TodoManualChecks ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        _suppressTodoStateWrite = true;
        try
        {
            foreach (var leaf in _todoLeafItems)
            {
                leaf.PropertyChanged -= OnTodoTaskPropertyChanged;
            }

            _todoItemsById.Clear();
            _todoLeafItems.Clear();
            TodoItems.Clear();

            foreach (var root in BuildTodoTree())
            {
                TodoItems.Add(root);
                RegisterTodoTaskRecursive(root);
            }

            foreach (var entry in _state.TodoManualChecks.Where(entry => entry.Value))
            {
                if (_todoItemsById.TryGetValue(entry.Key, out var node) && node.CanManuallyCheck)
                {
                    node.IsManualChecked = true;
                }
            }

            RefreshTodoAutoStates();
        }
        finally
        {
            _suppressTodoStateWrite = false;
        }
    }

    [RelayCommand]
    private void OpenZickchenTwitchLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.twitch.tv/Zickchen69",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusMessage = Lf("Could not open link: {0}", "Konnte Link nicht öffnen: {0}", exception.Message);
        }
    }

    private IReadOnlyList<TodoTaskViewModel> BuildTodoTree()
    {
        var phase1 = CreateTodoTask(
            "todo.phase1",
            "Phase 1: Day 1 Survival (sequential)",
            "Phase 1: Tag-1-Überleben (nacheinander)",
            "Secure basics first, then move out.",
            "Zuerst die Grundlagen sichern, dann weiterziehen.");
        phase1.AddChild(CreateTodoTask(
            "todo.weapon_bag",
            "Secure melee weapon + backpack",
            "Nahkampfwaffe + Rucksack sichern",
            "Do this before long looting routes.",
            "Vor längeren Loot-Routen abschließen."));
        phase1.AddChild(CreateTodoTask(
            "todo.safehouse",
            "Prepare temporary safehouse",
            "Temporäres Safehouse vorbereiten",
            "Curtains/sheets, sleeping spot, fallback exit.",
            "Vorhänge/Bettlaken, Schlafplatz, Fluchtweg."));
        phase1.AddChild(CreateTodoTask(
            "todo.water_food_tools",
            "Water reserve + can opener + basic meds",
            "Wasserreserve + Dosenöffner + Basis-Medizin",
            "Core items for the first days.",
            "Kern-Items für die ersten Tage."));

        var phase2 = CreateTodoTask(
            "todo.phase2",
            "Phase 2: First week XP window (parallel)",
            "Phase 2: Erste Woche XP-Fenster (parallel)",
            "These can be done in parallel while looting.",
            "Diese Schritte parallel während des Lootens abarbeiten.");
        phase2.AddChild(CreateTodoTask(
            "todo.life_and_living",
            "Watch Life and Living broadcasts",
            "Life and Living Sendungen schauen",
            "Time-sensitive XP during early days.",
            "Zeitkritischer XP-Boost in den ersten Tagen."));
        phase2.AddChild(CreateTodoTask(
            "todo.books_two",
            "At least 2 skill books completed/found",
            "Mindestens 2 Skill-Bücher erledigt/gefunden",
            "Auto from your Books tracker.",
            "Automatisch aus dem Bücher-Tracker."));
        phase2.AddChild(CreateTodoTask(
            "todo.magazines_one",
            "At least 1 magazine completed/found",
            "Mindestens 1 Magazin erledigt/gefunden",
            "Auto from your Magazine tracker.",
            "Automatisch aus dem Magazin-Tracker."));
        phase2.AddChild(CreateTodoTask(
            "todo.carpentry2",
            "Carpentry level >= 2",
            "Tischlerei >= 2",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase2.AddChild(CreateTodoTask(
            "todo.read_carpentry1",
            "Carpentry book level 1 covered",
            "Tischlerei-Buch Stufe 1 abgedeckt",
            "Auto by matching category and tier.",
            "Automatisch über Kategorie + Buchstufe."));
        phase2.AddChild(CreateTodoTask(
            "todo.read_cooking1",
            "Cooking book level 1 covered",
            "Koch-Buch Stufe 1 abgedeckt",
            "Auto by matching category and tier.",
            "Automatisch über Kategorie + Buchstufe."));
        phase2.AddChild(CreateTodoTask(
            "todo.read_mechanics1",
            "Mechanics book level 1 covered",
            "Mechanik-Buch Stufe 1 abgedeckt",
            "Auto by matching category and tier.",
            "Automatisch über Kategorie + Buchstufe."));

        var phase3 = CreateTodoTask(
            "todo.phase3",
            "Phase 3: Mobility and power (sequential)",
            "Phase 3: Mobilität und Strom (nacheinander)",
            "Unlock movement and long-term base utility.",
            "Mobilität und dauerhafte Basisversorgung freischalten.");
        phase3.AddChild(CreateTodoTask(
            "todo.hotwire",
            "Hotwire ready (Mechanics 2 + Electrical 1)",
            "Hotwire bereit (Mechanik 2 + Elektro 1)",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase3.AddChild(CreateTodoTask(
            "todo.generator_knowledge",
            "Generator knowledge (magazine or Electrical 3)",
            "Generator-Wissen (Magazin oder Elektro 3)",
            "Auto from magazine tracking / skills.",
            "Automatisch aus Magazin-Tracking / Skills."));
        phase3.AddChild(CreateTodoTask(
            "todo.rain_collector",
            "Build rain collector setup",
            "Regenwasser-Setup bauen",
            "Prepare before water shutoff.",
            "Vor Wasserabschaltung vorbereiten."));

        var phase4 = CreateTodoTask(
            "todo.phase4",
            "Phase 4: Knowledge loop (parallel)",
            "Phase 4: Wissens-Loop (parallel)",
            "Push your progression with books, magazines, recipes.",
            "Progression über Bücher, Magazine und Rezepte beschleunigen.");
        phase4.AddChild(CreateTodoTask(
            "todo.books_five",
            "At least 5 books completed",
            "Mindestens 5 Bücher erledigt",
            "Auto from Books tracker.",
            "Automatisch aus Bücher-Tracker."));
        phase4.AddChild(CreateTodoTask(
            "todo.magazines_three",
            "At least 3 magazines completed",
            "Mindestens 3 Magazine erledigt",
            "Auto from Magazine tracker.",
            "Automatisch aus Magazin-Tracker."));
        phase4.AddChild(CreateTodoTask(
            "todo.recipes_ten",
            "At least 10 recipes learned",
            "Mindestens 10 Rezepte gelernt",
            "Auto from Recipe tracker.",
            "Automatisch aus Rezepte-Tracker."));

        var phase5 = CreateTodoTask(
            "todo.phase5",
            "Phase 5: Stable long run (sequential)",
            "Phase 5: Stabiler Long-Run (nacheinander)",
            "Lock in repeatable survival systems.",
            "Wiederholbare Überlebenssysteme finalisieren.");
        phase5.AddChild(CreateTodoTask(
            "todo.carpentry4",
            "Carpentry level >= 4",
            "Tischlerei >= 4",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase5.AddChild(CreateTodoTask(
            "todo.cooking_or_farming",
            "Cooking >= 4 or Farming >= 2",
            "Kochen >= 4 oder Landwirtschaft >= 2",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase5.AddChild(CreateTodoTask(
            "todo.sustainable_base",
            "Vehicle + generator + water loop stable",
            "Fahrzeug + Generator + Wasserkreislauf stabil",
            "Final manual validation step.",
            "Finaler manueller Validierungsschritt."));

        var phase6 = CreateTodoTask(
            "todo.phase6",
            "Phase 6: Combat and logistics (parallel)",
            "Phase 6: Kampf und Logistik (parallel)",
            "Scale your combat reliability while keeping supplies stable.",
            "Baue Kampfstabilität aus und halte die Versorgung konstant.");
        phase6.AddChild(CreateTodoTask(
            "todo.maintenance3",
            "Maintenance >= 3",
            "Instandhaltung >= 3",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase6.AddChild(CreateTodoTask(
            "todo.aiming3_or_melee5",
            "Aiming >= 3 or any melee skill >= 5",
            "Zielen >= 3 oder ein Nahkampfskill >= 5",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase6.AddChild(CreateTodoTask(
            "todo.books_eight",
            "At least 8 books completed",
            "Mindestens 8 Bücher erledigt",
            "Auto from Books tracker.",
            "Automatisch aus Bücher-Tracker."));
        phase6.AddChild(CreateTodoTask(
            "todo.magazines_five",
            "At least 5 magazines completed",
            "Mindestens 5 Magazine erledigt",
            "Auto from Magazine tracker.",
            "Automatisch aus Magazin-Tracker."));
        phase6.AddChild(CreateTodoTask(
            "todo.recipes_twenty",
            "At least 20 recipes learned",
            "Mindestens 20 Rezepte gelernt",
            "Auto from Recipe tracker.",
            "Automatisch aus Rezepte-Tracker."));
        phase6.AddChild(CreateTodoTask(
            "todo.repair_stock",
            "Keep repair materials and backup weapons stocked",
            "Reparaturmaterial und Ersatzwaffen auf Vorrat halten",
            "Manual logistics validation step.",
            "Manueller Logistik-Check."));

        var phase7 = CreateTodoTask(
            "todo.phase7",
            "Phase 7: Infrastructure hardening (sequential)",
            "Phase 7: Infrastruktur absichern (nacheinander)",
            "Stabilize your long-run base systems.",
            "Langfristige Basis-Systeme absichern.");
        phase7.AddChild(CreateTodoTask(
            "todo.electrical4_or_metal4",
            "Electrical >= 4 or Metalworking >= 4",
            "Elektro >= 4 oder Metallbearbeitung >= 4",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase7.AddChild(CreateTodoTask(
            "todo.mechanics5",
            "Mechanics >= 5",
            "Mechanik >= 5",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase7.AddChild(CreateTodoTask(
            "todo.cooking6_or_farming4",
            "Cooking >= 6 or Farming >= 4",
            "Kochen >= 6 oder Landwirtschaft >= 4",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase7.AddChild(CreateTodoTask(
            "todo.firstaid3",
            "First Aid >= 3",
            "Erste Hilfe >= 3",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase7.AddChild(CreateTodoTask(
            "todo.second_base",
            "Establish secondary fallback location",
            "Zweiten Rückzugsort aufbauen",
            "Manual strategic fallback step.",
            "Manueller Strategie-Schritt."));

        var phase8 = CreateTodoTask(
            "todo.phase8",
            "Phase 8: Endgame mastery (parallel)",
            "Phase 8: Endgame-Meisterschaft (parallel)",
            "Finalize mastery goals and redundancy.",
            "Meisterschaftsziele und Redundanz finalisieren.");
        phase8.AddChild(CreateTodoTask(
            "todo.carpentry6",
            "Carpentry >= 6",
            "Tischlerei >= 6",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase8.AddChild(CreateTodoTask(
            "todo.books_twelve",
            "At least 12 books completed",
            "Mindestens 12 Bücher erledigt",
            "Auto from Books tracker.",
            "Automatisch aus Bücher-Tracker."));
        phase8.AddChild(CreateTodoTask(
            "todo.recipes_thirty",
            "At least 30 recipes learned",
            "Mindestens 30 Rezepte gelernt",
            "Auto from Recipe tracker.",
            "Automatisch aus Rezepte-Tracker."));
        phase8.AddChild(CreateTodoTask(
            "todo.backup_power_water",
            "Backup power + backup water ready",
            "Backup-Strom + Backup-Wasser bereit",
            "Manual resilience validation step.",
            "Manueller Resilienz-Check."));

        var phase9 = CreateTodoTask(
            "todo.phase9",
            "Phase 9: Seasonal self-sufficiency (sequential)",
            "Phase 9: Saisonale Selbstversorgung (nacheinander)",
            "Expand sustainable food and gear systems.",
            "Nachhaltige Versorgungs- und Ausrüstungssysteme ausbauen.");
        phase9.AddChild(CreateTodoTask(
            "todo.fishing4_or_trapping3",
            "Fishing >= 4 or Trapping >= 3",
            "Angeln >= 4 oder Fallenstellen >= 3",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase9.AddChild(CreateTodoTask(
            "todo.foraging5",
            "Foraging >= 5",
            "Sammeln >= 5",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase9.AddChild(CreateTodoTask(
            "todo.tailoring4",
            "Tailoring >= 4",
            "Schneidern >= 4",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase9.AddChild(CreateTodoTask(
            "todo.books_fifteen",
            "At least 15 books completed",
            "Mindestens 15 Bücher erledigt",
            "Auto from Books tracker.",
            "Automatisch aus Bücher-Tracker."));
        phase9.AddChild(CreateTodoTask(
            "todo.food_stock_month",
            "Build a one-month food reserve",
            "Einen Lebensmittelvorrat für einen Monat anlegen",
            "Manual sustainability validation step.",
            "Manueller Nachhaltigkeits-Check."));

        var phase10 = CreateTodoTask(
            "todo.phase10",
            "Phase 10: Mastery and redundancy (parallel)",
            "Phase 10: Meisterschaft und Redundanz (parallel)",
            "Harden your world state for long campaigns.",
            "Die Welt für sehr lange Runs absichern.");
        phase10.AddChild(CreateTodoTask(
            "todo.electrical6_or_metal6",
            "Electrical >= 6 or Metalworking >= 6",
            "Elektro >= 6 oder Metallbearbeitung >= 6",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase10.AddChild(CreateTodoTask(
            "todo.mechanics7",
            "Mechanics >= 7",
            "Mechanik >= 7",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase10.AddChild(CreateTodoTask(
            "todo.firstaid5_or_tailoring6",
            "First Aid >= 5 or Tailoring >= 6",
            "Erste Hilfe >= 5 oder Schneidern >= 6",
            "Auto from session skill levels.",
            "Automatisch aus Session-Skillleveln."));
        phase10.AddChild(CreateTodoTask(
            "todo.recipes_forty",
            "At least 40 recipes learned",
            "Mindestens 40 Rezepte gelernt",
            "Auto from Recipe tracker.",
            "Automatisch aus Rezepte-Tracker."));
        phase10.AddChild(CreateTodoTask(
            "todo.multibase_logistics",
            "Maintain logistics between multiple bases",
            "Logistik zwischen mehreren Basen aufrechterhalten",
            "Manual late-game validation step.",
            "Manueller Endgame-Check."));

        return [phase1, phase2, phase3, phase4, phase5, phase6, phase7, phase8, phase9, phase10];
    }

    private TodoTaskViewModel CreateTodoTask(
        string id,
        string englishTitle,
        string germanTitle,
        string englishDetail = "",
        string germanDetail = "")
    {
        return new TodoTaskViewModel(
            id,
            L(englishTitle, germanTitle),
            L(englishDetail, germanDetail),
            L("Auto", "Auto"),
            L("Manual", "Manuell"),
            L("Open", "Offen"));
    }

    private void RegisterTodoTaskRecursive(TodoTaskViewModel node)
    {
        _todoItemsById[node.Id] = node;

        if (node.CanManuallyCheck)
        {
            node.PropertyChanged += OnTodoTaskPropertyChanged;
            _todoLeafItems.Add(node);
        }

        foreach (var child in node.Children)
        {
            RegisterTodoTaskRecursive(child);
        }
    }

    private async void OnTodoTaskPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (_suppressTodoStateWrite ||
            sender is not TodoTaskViewModel task ||
            eventArgs.PropertyName != nameof(TodoTaskViewModel.IsManualChecked))
        {
            return;
        }

        if (task.IsManualChecked)
        {
            _state.TodoManualChecks[task.Id] = true;
        }
        else
        {
            _state.TodoManualChecks.Remove(task.Id);
        }

        UpdateTodoProgress();
        await SaveStateAsync();
    }

    private void RefreshTodoAutoStates()
    {
        if (_todoLeafItems.Count == 0)
        {
            UpdateTodoProgress();
            return;
        }

        foreach (var task in _todoLeafItems)
        {
            task.IsAutoCompleted = EvaluateTodoAutoCompletion(task.Id);
        }

        UpdateTodoProgress();
    }

    private void UpdateTodoProgress()
    {
        var total = _todoLeafItems.Count;
        if (total == 0)
        {
            TodoProgress = "0 / 0";
            return;
        }

        var done = _todoLeafItems.Count(task => task.IsCompleted);
        TodoProgress = $"{done} / {total}";
    }

    private bool EvaluateTodoAutoCompletion(string taskId)
    {
        return taskId switch
        {
            "todo.books_two" => CountCompletedBooks() >= 2,
            "todo.magazines_one" => CountCompletedMagazines() >= 1,
            "todo.carpentry2" => GetSkillLevel("carpentry", "woodwork") >= 2,
            "todo.read_carpentry1" => HasBookTierCompleted("carpentry", 1, "woodwork"),
            "todo.read_cooking1" => HasBookTierCompleted("cooking", 1),
            "todo.read_mechanics1" => HasBookTierCompleted("mechanics", 1, "mechanic"),
            "todo.hotwire" => GetSkillLevel("mechanics", "mechanic") >= 2 &&
                              GetSkillLevel("electrical", "electricity", "electrician") >= 1,
            "todo.generator_knowledge" => HasGeneratorKnowledge(),
            "todo.books_five" => CountCompletedBooks() >= 5,
            "todo.magazines_three" => CountCompletedMagazines() >= 3,
            "todo.recipes_ten" => CountCompletedRecipes() >= 10,
            "todo.carpentry4" => GetSkillLevel("carpentry", "woodwork") >= 4,
            "todo.cooking_or_farming" => GetSkillLevel("cooking") >= 4 || GetSkillLevel("farming") >= 2,
            "todo.maintenance3" => GetSkillLevel("maintenance") >= 3,
            "todo.aiming3_or_melee5" => GetSkillLevel("aiming") >= 3 ||
                                        GetSkillLevel("axe") >= 5 ||
                                        GetSkillLevel("longblunt") >= 5 ||
                                        GetSkillLevel("shortblunt") >= 5 ||
                                        GetSkillLevel("longblade") >= 5 ||
                                        GetSkillLevel("shortblade") >= 5 ||
                                        GetSkillLevel("spear") >= 5,
            "todo.books_eight" => CountCompletedBooks() >= 8,
            "todo.magazines_five" => CountCompletedMagazines() >= 5,
            "todo.recipes_twenty" => CountCompletedRecipes() >= 20,
            "todo.electrical4_or_metal4" => GetSkillLevel("electrical", "electricity", "electrician") >= 4 ||
                                            GetSkillLevel("metalworking", "metalwelding", "metalwork") >= 4,
            "todo.mechanics5" => GetSkillLevel("mechanics", "mechanic") >= 5,
            "todo.cooking6_or_farming4" => GetSkillLevel("cooking") >= 6 || GetSkillLevel("farming") >= 4,
            "todo.firstaid3" => GetSkillLevel("firstaid", "doctor") >= 3,
            "todo.carpentry6" => GetSkillLevel("carpentry", "woodwork") >= 6,
            "todo.books_twelve" => CountCompletedBooks() >= 12,
            "todo.recipes_thirty" => CountCompletedRecipes() >= 30,
            "todo.fishing4_or_trapping3" => GetSkillLevel("fishing") >= 4 || GetSkillLevel("trapping") >= 3,
            "todo.foraging5" => GetSkillLevel("foraging", "forage") >= 5,
            "todo.tailoring4" => GetSkillLevel("tailoring", "tailor") >= 4,
            "todo.books_fifteen" => CountCompletedBooks() >= 15,
            "todo.electrical6_or_metal6" => GetSkillLevel("electrical", "electricity", "electrician") >= 6 ||
                                            GetSkillLevel("metalworking", "metalwelding", "metalwork") >= 6,
            "todo.mechanics7" => GetSkillLevel("mechanics", "mechanic") >= 7,
            "todo.firstaid5_or_tailoring6" => GetSkillLevel("firstaid", "doctor") >= 5 ||
                                              GetSkillLevel("tailoring", "tailor") >= 6,
            "todo.recipes_forty" => CountCompletedRecipes() >= 40,
            _ => false,
        };
    }

    private bool HasGeneratorKnowledge()
    {
        if (GetSkillLevel("electrical", "electricity", "electrician") >= 3)
        {
            return true;
        }

        return _magazineItems.Any(item =>
            item.IsChecked &&
            (
                item.Name.Contains("generator", StringComparison.OrdinalIgnoreCase) ||
                item.GermanName.Contains("generator", StringComparison.OrdinalIgnoreCase) ||
                item.Detail.Contains("generator", StringComparison.OrdinalIgnoreCase)
            ));
    }

    private int CountCompletedBooks() => _bookItems.Count(item => item.IsChecked);

    private int CountCompletedMagazines() => _magazineItems.Count(item => item.IsChecked);

    private int CountCompletedRecipes() => _recipeItems.Count(item => item.IsChecked);

    private bool HasBookTierCompleted(string category, int level, params string[] additionalCategoryAliases)
    {
        var allAliases = new List<string> { category };
        allAliases.AddRange(additionalCategoryAliases);
        return _bookItems.Any(book =>
            book.IsChecked &&
            ResolveBookLevel(book) == level &&
            allAliases.Any(alias => NormalizeSkillKey(alias).Equals(NormalizeSkillKey(book.Category), StringComparison.OrdinalIgnoreCase)));
    }

    private int GetSkillLevel(params string[] categoryAliases)
    {
        if (categoryAliases.Length == 0 || SessionSkills.Count == 0)
        {
            return 0;
        }

        var levels = SessionSkills
            .GroupBy(skill => NormalizeSkillKey(skill.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Max(entry => entry.Level), StringComparer.OrdinalIgnoreCase);

        var aliases = categoryAliases
            .Select(alias => NormalizeSkillKey(alias))
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var alias in aliases.ToArray())
        {
            switch (alias)
            {
                case "woodwork":
                    aliases.Add("carpentry");
                    break;
                case "carpentry":
                    aliases.Add("woodwork");
                    break;
                case "electricity":
                case "electrician":
                    aliases.Add("electrical");
                    break;
                case "electrical":
                    aliases.Add("electricity");
                    aliases.Add("electrician");
                    break;
                case "mechanic":
                    aliases.Add("mechanics");
                    break;
                case "mechanics":
                    aliases.Add("mechanic");
                    break;
            }
        }

        var best = 0;
        foreach (var alias in aliases)
        {
            if (levels.TryGetValue(alias, out var level) && level > best)
            {
                best = level;
            }
        }

        return best;
    }

    private bool ShouldRunAutomaticSessionSync()
    {
        return AutoSessionSync || RiskIndicatorEnabled;
    }

    private void UpdateSessionPollingInterval()
    {
        _sessionTimer.Interval = RiskIndicatorEnabled
            ? TimeSpan.FromSeconds(1)
            : TimeSpan.FromMinutes(2);
    }

    private static DateTime ResolvePlayersDbLastWriteUtc(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return DateTime.MinValue;
        }

        var playersDbPath = Path.Combine(savePath, "players.db");
        return File.Exists(playersDbPath)
            ? File.GetLastWriteTimeUtc(playersDbPath)
            : DateTime.MinValue;
    }

    private bool HasPlayersDbChangedSinceLastSync()
    {
        var savePath = _sessionSyncService.TryResolveActiveSavePathForCurrentSession();
        var writeUtc = ResolvePlayersDbLastWriteUtc(savePath);
        if (writeUtc == DateTime.MinValue)
        {
            return false;
        }

        return writeUtc > _lastObservedPlayersDbWriteUtc;
    }

    private void ApplyRiskIndicator(SessionSyncResult result)
    {
        RiskLevel = result.RiskLevel;
        RiskScore = result.RiskScore;
        RiskNotes = BuildLocalizedRiskNotes(result);
    }

    private void ResetRiskIndicator()
    {
        RiskLevel = SessionRiskLevel.Unknown;
        RiskScore = 0;
        RiskNotes = string.Empty;
    }

    private string BuildLocalizedRiskNotes(SessionSyncResult result)
    {
        var notes = new List<string>();
        if (result.InjuryRiskScore >= 20)
        {
            notes.Add(L("Injuries", "Verletzungen"));
        }

        if (result.MoodleRiskScore >= 18)
        {
            notes.Add(L("Bad moodles", "Schlechte Moodles"));
        }

        if (result.ExhaustionRiskScore >= 15)
        {
            notes.Add(L("Exhaustion", "Erschöpfung"));
        }

        if (result.FoodRiskScore >= 20)
        {
            notes.Add(L("Low food or water", "Wenig Essen oder Wasser"));
        }

        if (result.WeightRiskScore >= 10)
        {
            notes.Add(L("Weight warning", "Gewichts-Warnung"));
        }

        if (notes.Count == 0)
        {
            notes.Add(L("No major warnings", "Keine größeren Warnungen"));
        }

        return Lf("Signals: {0}", "Signale: {0}", string.Join(", ", notes));
    }

    private async void SessionTimerOnTick(object? sender, EventArgs eventArgs)
    {
        if (!ShouldRunAutomaticSessionSync() || IsBusy)
        {
            return;
        }

        if (RiskIndicatorEnabled && !HasPlayersDbChangedSinceLastSync())
        {
            return;
        }

        ConfigureSessionWatcher();
        await SyncSessionAsync(isManual: false);
    }

    private async void SessionWatcherDebounceOnTick(object? sender, EventArgs eventArgs)
    {
        _sessionWatcherDebounceTimer.Stop();
        if (!_sessionWatcherDirty || !ShouldRunAutomaticSessionSync() || IsBusy)
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
        if (ShouldRunAutomaticSessionSync())
        {
            await SyncSessionAsync(isManual: false);
        }
    }

    private async Task RefreshAvailableLanguagesAsync()
    {
        var gameLanguages = await Task.Run(() => _guideCatalogService.GetAvailableLanguageCodes(GamePath, IncludeMods));
        var allLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in _uiLocalizationService.GetSupportedLanguageCodes())
        {
            allLanguages.Add(NormalizeLanguageCode(language));
        }

        foreach (var language in gameLanguages)
        {
            allLanguages.Add(NormalizeLanguageCode(language));
        }

        var options = allLanguages
            .OrderBy(code => code.Equals("EN", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
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

        var selectedCode = ResolvePreferredLanguageCode(
            _state.LanguageCode,
            AvailableLanguages.Select(option => option.Code).ToList());
        if (!selectedCode.Equals(_state.LanguageCode, StringComparison.OrdinalIgnoreCase))
        {
            _state.LanguageCode = selectedCode;
            ApplyUiLanguage();
            await SaveStateAsync();
        }

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

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "EN";
        }

        var normalized = languageCode
            .Trim()
            .ToUpperInvariant()
            .Replace("-", string.Empty)
            .Replace("_", string.Empty);

        return normalized switch
        {
            "PTB" => "PTBR",
            "UK" => "UA",
            _ => normalized,
        };
    }

    private static string DetectSystemLanguageCode()
    {
        var uiLanguage = MapCultureToLanguageCode(CultureInfo.CurrentUICulture);
        if (!string.IsNullOrWhiteSpace(uiLanguage))
        {
            return uiLanguage;
        }

        var currentLanguage = MapCultureToLanguageCode(CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(currentLanguage)
            ? "EN"
            : currentLanguage;
    }

    private static string MapCultureToLanguageCode(CultureInfo? culture)
    {
        if (culture is null)
        {
            return "EN";
        }

        var cultureName = (culture.Name ?? string.Empty).Trim().Replace('_', '-');
        var upperName = cultureName.ToUpperInvariant();
        var nameParts = upperName.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var primary = nameParts.Length > 0 ? nameParts[0] : string.Empty;
        var secondary = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        if (primary.Equals("ZH", StringComparison.Ordinal))
        {
            var isTraditionalChinese = secondary is "TW" or "HK" or "MO" || upperName.Contains("HANT", StringComparison.Ordinal);
            return isTraditionalChinese ? "CH" : "CN";
        }

        if (primary.Equals("PT", StringComparison.Ordinal) && secondary.Equals("BR", StringComparison.Ordinal))
        {
            return "PTBR";
        }

        return primary switch
        {
            "" or "IV" => "EN",
            "JA" => "JP",
            "UK" => "UA",
            "NB" or "NN" => "NO",
            "TL" or "FIL" => "PH",
            _ => NormalizeLanguageCode(primary),
        };
    }

    private static string ResolvePreferredLanguageCode(string? savedLanguageCode, IReadOnlyCollection<string> availableLanguageCodes)
    {
        var available = availableLanguageCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(NormalizeLanguageCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (available.Count == 0)
        {
            return "EN";
        }

        if (!string.IsNullOrWhiteSpace(savedLanguageCode))
        {
            var normalizedSavedLanguage = NormalizeLanguageCode(savedLanguageCode);
            if (available.Contains(normalizedSavedLanguage))
            {
                return normalizedSavedLanguage;
            }
        }

        var systemLanguage = DetectSystemLanguageCode();
        if (available.Contains(systemLanguage))
        {
            return systemLanguage;
        }

        return available.Contains("EN")
            ? "EN"
            : available.First();
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
                    ("obsolete", L("No Longer Needed", "Nicht mehr benötigt")),
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
        SessionSkillsHeader = Lf("Session Skills ({0})", "Session-Skills ({0})", SessionSkills.Count);

        LastSyncText = _state.LastSyncAt.HasValue
            ? Lf("Last catalog sync: {0:dd.MM.yyyy HH:mm}", "Letzte Katalog-Sync: {0:dd.MM.yyyy HH:mm}", _state.LastSyncAt)
            : L("No catalog sync yet", "Noch keine Katalog-Synchronisierung");

        LastSessionSyncText = _state.LastSessionSyncAt.HasValue
            ? Lf("Last session sync: {0:dd.MM.yyyy HH:mm}", "Letzte Session-Sync: {0:dd.MM.yyyy HH:mm}", _state.LastSessionSyncAt)
            : L("No session sync yet", "Noch keine Session-Synchronisierung");

        if (_latestUpdateResult is null)
        {
            UpdateStatusMessage = L("Update: not checked yet", "Update: noch nicht geprüft");
        }

        if (_catalogItems.Count == 0)
        {
            DataSource = L("Not loaded yet", "Noch nicht geladen");
        }

        OnPropertyChanged(nameof(HeaderSubtitleText));
        OnPropertyChanged(nameof(HeaderTitleText));
        OnPropertyChanged(nameof(WindowTitleText));
        OnPropertyChanged(nameof(GamePathWatermarkText));
        OnPropertyChanged(nameof(ModsOffText));
        OnPropertyChanged(nameof(ModsOnText));
        OnPropertyChanged(nameof(AutoDetectPathButtonText));
        OnPropertyChanged(nameof(LoadFromGameButtonText));
        OnPropertyChanged(nameof(LoadFallbackButtonText));
        OnPropertyChanged(nameof(SyncSessionButtonText));
        OnPropertyChanged(nameof(DiagnosticsButtonText));
        OnPropertyChanged(nameof(SearchWatermarkText));
        OnPropertyChanged(nameof(AutoSessionSyncOffText));
        OnPropertyChanged(nameof(AutoSessionSyncOnText));
        OnPropertyChanged(nameof(ClearChecksButtonText));
        OnPropertyChanged(nameof(AutoUpdateOffText));
        OnPropertyChanged(nameof(AutoUpdateOnText));
        OnPropertyChanged(nameof(RiskIndicatorOffText));
        OnPropertyChanged(nameof(RiskIndicatorOnText));
        OnPropertyChanged(nameof(RiskIndicatorTitleText));
        OnPropertyChanged(nameof(RiskLevelText));
        OnPropertyChanged(nameof(RiskScoreText));
        OnPropertyChanged(nameof(RiskHintText));
        OnPropertyChanged(nameof(RiskBadgeBrush));
        OnPropertyChanged(nameof(CheckUpdatesButtonText));
        OnPropertyChanged(nameof(InstallUpdateButtonText));
        OnPropertyChanged(nameof(DismissUpdatePromptButtonText));
        OnPropertyChanged(nameof(UpdatePromptTitleText));
        OnPropertyChanged(nameof(UpdatePromptMessageText));
        OnPropertyChanged(nameof(ReleaseVersionLabelText));
        OnPropertyChanged(nameof(LanguageLabelText));
        OnPropertyChanged(nameof(BookFilterLabelText));
        OnPropertyChanged(nameof(MagazineFilterLabelText));
        OnPropertyChanged(nameof(RecipeFilterLabelText));
        OnPropertyChanged(nameof(DiagnosticsDialogTitleText));
        OnPropertyChanged(nameof(CloseDiagnosticsButtonText));
        OnPropertyChanged(nameof(TodoSubtitleText));
        OnPropertyChanged(nameof(TwitchButtonText));
        OnPropertyChanged(nameof(TwitchButtonTextZickchen));
        OnPropertyChanged(nameof(BooksTabHeader));
        OnPropertyChanged(nameof(MagazinesTabHeader));
        OnPropertyChanged(nameof(RecipesTabHeader));
        OnPropertyChanged(nameof(TodoTabHeader));
        RebuildTodoPlan();
        UpdateReleaseVersionText();
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

    private void UpdateReleaseVersionText()
    {
        var version = string.IsNullOrWhiteSpace(_state.LastKnownReleaseVersion)
            ? "-"
            : _state.LastKnownReleaseVersion;

        ReleaseVersionText = $"{ReleaseVersionLabelText}: {version}";
    }

    private string L(string english, string german)
    {
        return _uiLocalizationService.Translate(_state.LanguageCode, english, german);
    }

    private string Lf(string englishTemplate, string germanTemplate, params object?[] args)
    {
        var localizedTemplate = L(englishTemplate, germanTemplate);
        return string.Format(CultureInfo.CurrentCulture, localizedTemplate, args);
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
            _state.LanguageCode = NormalizeLanguageCode(_state.LanguageCode);
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
            _state.TodoManualChecks = _state.TodoManualChecks
                .Where(entry => entry.Value && !string.IsNullOrWhiteSpace(entry.Key))
                .ToDictionary(entry => entry.Key, entry => true, StringComparer.OrdinalIgnoreCase);
            await _appStateService.SaveAsync(_state);
        }
        catch
        {
            // Persist errors should not crash the UI.
        }
    }

    private static Version GetCurrentAppVersion()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(processPath).FileVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion))
            {
                var cleanFileVersion = fileVersion.Split('+')[0];
                var dashIndex = cleanFileVersion.IndexOf('-');
                if (dashIndex >= 0)
                {
                    cleanFileVersion = cleanFileVersion[..dashIndex];
                }

                if (Version.TryParse(cleanFileVersion, out var parsedFileVersion))
                {
                    return parsedFileVersion;
                }
            }
        }

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

    private static string TruncateStatusDetail(string? value, int maxLength = 700)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..maxLength] + "...";
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
