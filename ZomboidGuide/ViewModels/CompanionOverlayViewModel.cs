using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZomboidGuide.Services;

namespace ZomboidGuide.ViewModels;

public partial class CompanionOverlayViewModel : ViewModelBase
{
    private const int DefaultPort = 8765;
    private readonly LocalHttpServer _localHttpServer;
    private readonly UiLocalizationService? _uiLocalizationService;
    private readonly Func<string>? _languageCodeProvider;
    private bool _suppressSettingsChanged;

    public event Action<int, bool, bool>? OverlaySettingsChanged;

    public CompanionOverlayViewModel(
        LocalHttpServer localHttpServer,
        UiLocalizationService? uiLocalizationService = null,
        Func<string>? languageCodeProvider = null)
    {
        _localHttpServer = localHttpServer;
        _uiLocalizationService = uiLocalizationService;
        _languageCodeProvider = languageCodeProvider;
        OverlayUrl = BuildOverlayUrl(LocalHttpServer.ResolvePreferredHostAddress(), DefaultPort);
        ApplyLocalization();
        UpdateState();
    }

    [ObservableProperty]
    private string title = "Overlay";

    [ObservableProperty]
    private string portText = "8765";

    [ObservableProperty]
    private bool autoStart;

    [ObservableProperty]
    private bool rotateSlides = true;

    [ObservableProperty]
    private bool isServerRunning;

    [ObservableProperty]
    private string statusText = "Stopped";

    [ObservableProperty]
    private string overlayUrl = $"http://{LocalHttpServer.ResolvePreferredHostAddress()}:8765/overlay";

    [ObservableProperty]
    private string portLabelText = "Port";

    [ObservableProperty]
    private string autoStartOffText = "Autostart Off";

    [ObservableProperty]
    private string autoStartOnText = "Autostart On";

    [ObservableProperty]
    private string rotateOffText = "Static View";

    [ObservableProperty]
    private string rotateOnText = "Rotate Every 10s";

    [ObservableProperty]
    private string obsUrlLabelText = "OBS URL";

    [ObservableProperty]
    private string copyButtonText = "Copy";

    [ObservableProperty]
    private string openButtonText = "Open";

    [ObservableProperty]
    private string startButtonText = "Start";

    [ObservableProperty]
    private string stopButtonText = "Stop";

    public bool IsServerStopped => !IsServerRunning;

    public string ToggleServerButtonText => IsServerRunning ? StopButtonText : StartButtonText;

    partial void OnPortTextChanged(string value)
    {
        if (IsServerRunning)
        {
            return;
        }

        if (TryParsePort(value, out var port))
        {
            OverlayUrl = BuildOverlayUrl(LocalHttpServer.ResolvePreferredHostAddress(), port);
            PublishSettingsChanged();
        }
    }

    partial void OnAutoStartChanged(bool value)
    {
        PublishSettingsChanged();
    }

    partial void OnRotateSlidesChanged(bool value)
    {
        PublishSettingsChanged();
    }

    partial void OnIsServerRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsServerStopped));
        OnPropertyChanged(nameof(ToggleServerButtonText));
    }

    public void ApplyLocalization()
    {
        Title = T("Overlay", "Overlay");
        PortLabelText = T("Port", "Port");
        AutoStartOffText = T("Autostart Off", "Autostart Aus");
        AutoStartOnText = T("Autostart On", "Autostart An");
        RotateOffText = T("Static View", "Statische Ansicht");
        RotateOnText = T("Rotate Every 10s", "Rotation alle 10s");
        ObsUrlLabelText = T("OBS URL", "OBS URL");
        CopyButtonText = T("Copy", "Kopieren");
        OpenButtonText = T("Open", "Öffnen");
        StartButtonText = T("Start", "Start");
        StopButtonText = T("Stop", "Stop");
        OnPropertyChanged(nameof(ToggleServerButtonText));
        StatusText = _localHttpServer.IsRunning
            ? RunningOn(OverlayUrl)
            : Stopped();
    }

    [RelayCommand]
    private void ToggleServer()
    {
        if (IsServerRunning)
        {
            StopServer();
            return;
        }

        StartServer();
    }

    public void ApplySettings(int port, bool autoStart, bool rotateSlides)
    {
        var normalizedPort = NormalizePort(port);
        _suppressSettingsChanged = true;
        try
        {
            PortText = normalizedPort.ToString(CultureInfo.InvariantCulture);
            AutoStart = autoStart;
            RotateSlides = rotateSlides;
        }
        finally
        {
            _suppressSettingsChanged = false;
        }

        OverlayUrl = BuildOverlayUrl(LocalHttpServer.ResolvePreferredHostAddress(), normalizedPort);
        LocalHttpServer.TryStopForeignServerIfDifferentSession(normalizedPort);
        if (AutoStart && !IsServerRunning)
        {
            StartServer();
        }
    }

    public int GetCurrentPortOrDefault()
    {
        return TryParsePort(PortText, out var port)
            ? port
            : DefaultPort;
    }

    private void StartServer()
    {
        if (!TryParsePort(PortText, out var port))
        {
            StatusText = T("Invalid port (1-65535).", "Ungültiger Port (1-65535).");
            return;
        }

        try
        {
            _localHttpServer.Start(port);
            OverlayUrl = BuildOverlayUrl(_localHttpServer.HostAddress, port);
            UpdateState();
            StatusText = RunningOn(OverlayUrl);
            PublishSettingsChanged();
        }
        catch (Exception exception)
        {
            UpdateState();
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                T("Failed to start: {0}", "Start fehlgeschlagen: {0}"),
                exception.Message);
        }
    }

    [RelayCommand]
    public void StopServer()
    {
        _localHttpServer.Stop();
        UpdateState();
        StatusText = Stopped();
    }

    [RelayCommand]
    private void OpenOverlayInBrowser()
    {
        if (!_localHttpServer.IsRunning)
        {
            StatusText = T("Server is not running.", "Server läuft nicht.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OverlayUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                T("Open failed: {0}", "Öffnen fehlgeschlagen: {0}"),
                exception.Message);
        }
    }

    [RelayCommand]
    private async Task CopyOverlayUrlAsync()
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            StatusText = T("Clipboard unavailable.", "Zwischenablage nicht verfügbar.");
            return;
        }

        await clipboard.SetTextAsync(OverlayUrl);
        StatusText = T("Overlay URL copied.", "Overlay-URL kopiert.");
    }

    private void UpdateState()
    {
        IsServerRunning = _localHttpServer.IsRunning;
        if (_localHttpServer.IsRunning && _localHttpServer.Port > 0)
        {
            OverlayUrl = BuildOverlayUrl(_localHttpServer.HostAddress, _localHttpServer.Port);
            return;
        }

        if (TryParsePort(PortText, out var configuredPort))
        {
            OverlayUrl = BuildOverlayUrl(LocalHttpServer.ResolvePreferredHostAddress(), configuredPort);
        }
    }

    private void PublishSettingsChanged()
    {
        if (_suppressSettingsChanged)
        {
            return;
        }

        OverlaySettingsChanged?.Invoke(GetCurrentPortOrDefault(), AutoStart, RotateSlides);
    }

    private string RunningOn(string url)
    {
        return string.Format(CultureInfo.CurrentCulture, T("Running on {0}", "Läuft auf {0}"), url);
    }

    private string Stopped()
    {
        return T("Stopped", "Gestoppt");
    }

    private string T(string english, string german)
    {
        var languageCode = _languageCodeProvider?.Invoke();
        return _uiLocalizationService?.Translate(languageCode, english, german) ?? english;
    }

    private static bool TryParsePort(string value, out int port)
    {
        var ok = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        return ok && port is >= 1 and <= 65535;
    }

    private static int NormalizePort(int port)
    {
        return port is >= 1 and <= 65535
            ? port
            : DefaultPort;
    }

    private static string BuildOverlayUrl(string hostAddress, int port)
    {
        return $"http://{hostAddress}:{port}/overlay";
    }
}
