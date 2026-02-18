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
    private bool _suppressSettingsChanged;

    public event Action<int, bool>? OverlaySettingsChanged;

    public CompanionOverlayViewModel(LocalHttpServer localHttpServer)
    {
        _localHttpServer = localHttpServer;
        OverlayUrl = BuildOverlayUrl(LocalHttpServer.ResolvePreferredHostAddress(), DefaultPort);
        UpdateState();
    }

    [ObservableProperty]
    private string title = "Overlay";

    [ObservableProperty]
    private string portText = "8765";

    [ObservableProperty]
    private bool autoStart;

    [ObservableProperty]
    private bool isServerRunning;

    [ObservableProperty]
    private string statusText = "Stopped";

    [ObservableProperty]
    private string overlayUrl = $"http://{LocalHttpServer.ResolvePreferredHostAddress()}:8765/overlay";

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

    partial void OnIsServerRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsServerStopped));
        OnPropertyChanged(nameof(ToggleServerButtonText));
    }

    public bool IsServerStopped => !IsServerRunning;

    public string ToggleServerButtonText => IsServerRunning ? "Stop" : "Start";

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

    public void ApplySettings(int port, bool autoStart)
    {
        var normalizedPort = NormalizePort(port);
        _suppressSettingsChanged = true;
        try
        {
            PortText = normalizedPort.ToString(CultureInfo.InvariantCulture);
            AutoStart = autoStart;
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
            StatusText = "Invalid port (1-65535).";
            return;
        }

        try
        {
            _localHttpServer.Start(port);
            OverlayUrl = BuildOverlayUrl(_localHttpServer.HostAddress, port);
            UpdateState();
            StatusText = $"Running on {OverlayUrl}";
            PublishSettingsChanged();
        }
        catch (Exception exception)
        {
            UpdateState();
            StatusText = $"Failed to start: {exception.Message}";
        }
    }

    [RelayCommand]
    public void StopServer()
    {
        _localHttpServer.Stop();
        UpdateState();
        StatusText = "Stopped";
    }

    [RelayCommand]
    private void OpenOverlayInBrowser()
    {
        if (!_localHttpServer.IsRunning)
        {
            StatusText = "Server is not running.";
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
            StatusText = $"Open failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task CopyOverlayUrlAsync()
    {
        var clipboard = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            StatusText = "Clipboard unavailable.";
            return;
        }

        await clipboard.SetTextAsync(OverlayUrl);
        StatusText = "Overlay URL copied.";
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

        OverlaySettingsChanged?.Invoke(GetCurrentPortOrDefault(), AutoStart);
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
