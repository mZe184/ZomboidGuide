using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace ZomboidGuide.Services;

public sealed class LocalHttpServer : IDisposable
{
    private const string ServerAppId = "zomboidguide-overlay";
    private static readonly string CurrentSessionId = Guid.NewGuid().ToString("N");
    private static readonly object GlobalSync = new();
    private static readonly HashSet<LocalHttpServer> ActiveInstances = [];
    private static readonly HttpClient ProbeClient = new()
    {
        Timeout = TimeSpan.FromMilliseconds(250),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    private readonly OverlayStateProvider _stateProvider;
    private readonly Func<string>? _gamePathProvider;
    private readonly object _sync = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public LocalHttpServer(OverlayStateProvider stateProvider, Func<string>? gamePathProvider = null)
    {
        _stateProvider = stateProvider;
        _gamePathProvider = gamePathProvider;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _listener is not null;
            }
        }
    }

    public int Port { get; private set; }

    public string HostAddress { get; private set; } = ResolvePreferredHostAddress();

    public string SessionId => CurrentSessionId;

    public void Start(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be in range 1..65535.");
        }

        var hostAddress = ResolvePreferredHostAddress();

        lock (_sync)
        {
            if (_listener is not null &&
                Port == port &&
                string.Equals(HostAddress, hostAddress, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        Stop();
        TryStopForeignServerIfDifferentSession(port);

        var listener = new TcpListener(IPAddress.Any, port);

        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            listener.Stop();
            throw new InvalidOperationException($"Could not start local overlay server on port {port}: {exception.Message}", exception);
        }

        var cts = new CancellationTokenSource();
        var acceptLoop = Task.Run(() => AcceptLoopAsync(listener, cts.Token), cts.Token);

        lock (_sync)
        {
            _listener = listener;
            _cts = cts;
            _acceptLoop = acceptLoop;
            Port = port;
            HostAddress = hostAddress;
        }

        lock (GlobalSync)
        {
            ActiveInstances.Add(this);
        }
    }

    public void Stop()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        Task? acceptLoop;

        lock (_sync)
        {
            listener = _listener;
            cts = _cts;
            acceptLoop = _acceptLoop;
            _listener = null;
            _cts = null;
            _acceptLoop = null;
            Port = 0;
            HostAddress = ResolvePreferredHostAddress();
        }

        lock (GlobalSync)
        {
            ActiveInstances.Remove(this);
        }

        try
        {
            cts?.Cancel();
        }
        catch
        {
            // Ignore shutdown race errors.
        }

        try
        {
            listener?.Stop();
        }
        catch
        {
            // Ignore shutdown race errors.
        }

        try
        {
            acceptLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore loop completion failures during shutdown.
        }

        cts?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    public static void StopAllInstances()
    {
        LocalHttpServer[] snapshot;
        lock (GlobalSync)
        {
            snapshot = ActiveInstances.ToArray();
            ActiveInstances.Clear();
        }

        foreach (var instance in snapshot)
        {
            try
            {
                instance.Stop();
            }
            catch
            {
                // Ignore shutdown failures during process teardown.
            }
        }
    }

    public static string ResolvePreferredHostAddress()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(networkInterface =>
                    networkInterface.OperationalStatus == OperationalStatus.Up &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Select(unicast => unicast.Address)
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(address => address.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return addresses.Count == 0
                ? IPAddress.Loopback.ToString()
                : addresses[0];
        }
        catch
        {
            return IPAddress.Loopback.ToString();
        }
    }

    public static void TryStopForeignServerIfDifferentSession(int port)
    {
        if (port is < 1 or > 65535)
        {
            return;
        }

        if (!TryReadRemoteIdentity(port, out var identity))
        {
            if (!IsPortOpenOnLoopback(port))
            {
                return;
            }

            TryKillLikelyForeignZomboidGuideProcess();
            WaitForPortToClose(port, TimeSpan.FromSeconds(2));
            return;
        }

        if (!string.Equals(identity.App, ServerAppId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(identity.SessionId, CurrentSessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var shutdownRequested = TryRequestRemoteShutdown(port);
        if (!shutdownRequested &&
            identity.ProcessId > 0 &&
            identity.ProcessId != Environment.ProcessId)
        {
            TryKillProcess(identity.ProcessId);
        }

        WaitForPortToClose(port, TimeSpan.FromSeconds(2));
    }

    private static void TryKillLikelyForeignZomboidGuideProcess()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.HasExited)
                {
                    continue;
                }

                var name = process.ProcessName ?? string.Empty;
                if (!name.Contains("zomboidguide", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: false);
                process.WaitForExit(1200);
                return;
            }
            catch
            {
                // Ignore inspection/termination errors and continue scanning.
            }
        }
    }

    private static bool TryReadRemoteIdentity(int port, out ServerIdentityPayload identity)
    {
        identity = new ServerIdentityPayload();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/server");
            using var response = ProbeClient.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonSerializer.Deserialize<ServerIdentityPayload>(json, JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.App))
            {
                return false;
            }

            identity = payload;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRequestRemoteShutdown(int port)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/terminate");
            using var response = ProbeClient.Send(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void TryKillProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: false);
            process.WaitForExit(1200);
        }
        catch
        {
            // Ignore process termination failures and continue startup.
        }
    }

    private static void WaitForPortToClose(int port, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (!IsPortOpenOnLoopback(port))
            {
                return;
            }

            Thread.Sleep(80);
        }
    }

    private static bool IsPortOpenOnLoopback(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            return connectTask.Wait(120) && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, cancellationToken).ConfigureAwait(false);
                var method = ExtractMethod(requestLine);
                var path = ExtractPath(requestLine);

                if (path.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = _stateProvider.GetState();
                    var json = JsonSerializer.Serialize(payload, JsonOptions);
                    await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
                    return;
                }

                const string moodleIconPrefix = "/api/moodle-icon/";
                if (path.StartsWith(moodleIconPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var iconFileName = path[moodleIconPrefix.Length..].Trim();
                    var gamePath = _gamePathProvider?.Invoke();
                    if (!string.IsNullOrWhiteSpace(iconFileName) &&
                        MoodleIconResolver.TryResolveIconPath(gamePath, iconFileName, out var iconPath))
                    {
                        byte[] iconBytes;
                        try
                        {
                            iconBytes = File.ReadAllBytes(iconPath);
                        }
                        catch
                        {
                            iconBytes = [];
                        }

                        if (iconBytes.Length > 0)
                        {
                            await WriteBinaryResponseAsync(stream, 200, "OK", "image/png", iconBytes, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }

                    await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Moodle icon not found.", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (path.Equals("/api/server", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = new ServerIdentityPayload
                    {
                        App = ServerAppId,
                        SessionId = CurrentSessionId,
                        ProcessId = Environment.ProcessId,
                        Port = Port,
                    };
                    var json = JsonSerializer.Serialize(payload, JsonOptions);
                    await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", json, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if ((path.Equals("/api/terminate", StringComparison.OrdinalIgnoreCase) ||
                     path.Equals("/api/shutdown", StringComparison.OrdinalIgnoreCase)) &&
                    (method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                     method.Equals("GET", StringComparison.OrdinalIgnoreCase)))
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            Thread.Sleep(120);
                            Stop();
                        }
                        catch
                        {
                            // Ignore delayed shutdown failures.
                        }
                    }, CancellationToken.None);

                    await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", "{\"ok\":true}", cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (path.Equals("/", StringComparison.OrdinalIgnoreCase) || path.Equals("/overlay", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", OverlayHtml, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not found.", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Ignore per-request failures.
            }
        }
    }

    private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        await using var memory = new MemoryStream();
        while (memory.Length < 8192)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            var text = Encoding.ASCII.GetString(memory.GetBuffer(), 0, (int)memory.Length);
            var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                break;
            }
        }

        var requestText = Encoding.ASCII.GetString(memory.ToArray());
        var requestLineEnd = requestText.IndexOf("\r\n", StringComparison.Ordinal);
        return requestLineEnd > 0
            ? requestText[..requestLineEnd]
            : requestText;
    }

    private static string ExtractPath(string requestLine)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return "/";
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return "/";
        }

        var rawPath = parts[1];
        var queryIndex = rawPath.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            rawPath = rawPath[..queryIndex];
        }

        if (Uri.TryCreate(rawPath, UriKind.Absolute, out var absoluteUri))
        {
            rawPath = absoluteUri.AbsolutePath;
        }

        rawPath = Uri.UnescapeDataString(rawPath).TrimEnd('/');
        return string.IsNullOrWhiteSpace(rawPath) ? "/" : rawPath;
    }

    private static string ExtractMethod(string requestLine)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return "GET";
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? "GET"
            : parts[0].Trim();
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        string contentType,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header =
            $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBinaryResponseAsync(
        NetworkStream stream,
        int statusCode,
        string reasonPhrase,
        string contentType,
        byte[] bodyBytes,
        CancellationToken cancellationToken)
    {
        var header =
            $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: public, max-age=86400\r\n" +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed class ServerIdentityPayload
    {
        public string App { get; init; } = string.Empty;

        public string SessionId { get; init; } = string.Empty;

        public int ProcessId { get; init; }

        public int Port { get; init; }
    }

    private const string OverlayHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>ZomboidGuide Overlay</title>
  <style>
    :root {
      --bg: #0f1217;
      --panel: #171c25;
      --text: #eef2f7;
      --muted: #a6b1c2;
      --accent: #2d89ef;
      --good: #2f9e44;
      --warn: #f08c00;
      --bad: #c92a2a;
      --bar-bg: #242b37;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      padding: 10px;
      font-family: "Segoe UI", Tahoma, sans-serif;
      color: var(--text);
      background: transparent;
    }
    .panel {
      width: 420px;
      background: rgba(23, 28, 37, 0.88);
      border: 1px solid rgba(255, 255, 255, 0.14);
      border-radius: 10px;
      padding: 10px;
      backdrop-filter: blur(2px);
    }
    .header {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      margin-bottom: 8px;
      font-size: 13px;
    }
    .muted { color: var(--muted); }
    .row {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 8px;
      font-size: 13px;
      margin: 4px 0;
    }
    .danger-pill {
      border-radius: 999px;
      padding: 3px 8px;
      font-size: 12px;
      font-weight: 700;
      color: #fff;
      min-width: 90px;
      text-align: center;
    }
    .pill-green { background: var(--good); }
    .pill-yellow { background: var(--warn); }
    .pill-orange { background: #d9732f; }
    .pill-red { background: var(--bad); }
    .bars {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 6px;
      margin-top: 8px;
    }
    .bar-box {
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
      padding: 6px;
    }
    .bar-head {
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      margin-bottom: 4px;
    }
    .bar-bg {
      width: 100%;
      height: 8px;
      background: var(--bar-bg);
      border-radius: 999px;
      overflow: hidden;
    }
    .bar-fill {
      height: 100%;
      width: 0%;
      background: linear-gradient(90deg, #4dabf7, #2d89ef);
      transition: width .25s linear;
    }
    .moodles-strip {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 4px;
      min-height: 64px;
    }
    .moodle-icon {
      width: 60px;
      height: 60px;
      border-radius: 4px;
      border: 1px solid rgba(255, 255, 255, 0.16);
      background: rgba(255, 255, 255, 0.04);
      padding: 2px;
      object-fit: contain;
    }
  </style>
</head>
<body>
  <div class="panel">
    <div class="header">
      <div><strong>ZomboidGuide</strong> <span class="muted" id="runId">run-unknown</span></div>
      <div class="muted" id="worldTime">-</div>
    </div>

    <div class="row"><span id="labelKillsTotal">Kills Total</span><strong id="killsTotal">0</strong></div>
    <div class="row"><span id="labelKillsThisSession">Kills This Session</span><strong id="killsThisSession">0</strong></div>
    <div class="row"><span id="labelKillsPerHour">Kills / Hour (played)</span><strong id="killsPerHour">0.0</strong></div>
    <div class="row"><span id="labelTimeSurvived">Time Survived</span><strong id="timeSurvived">0d 00h 00m</strong></div>
    <div class="row">
      <span id="labelDanger">Danger Level</span>
      <span id="dangerPill" class="danger-pill pill-green">SAFE (0)</span>
    </div>

    <div class="bars">
      <div id="fatigue" class="bar-box">
        <div class="bar-head"><span id="labelFatigue">Fatigue</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="tiredness" class="bar-box">
        <div class="bar-head"><span id="labelTiredness">Tiredness</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="endurance" class="bar-box">
        <div class="bar-head"><span id="labelEndurance">Endurance</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="hunger" class="bar-box">
        <div class="bar-head"><span id="labelHunger">Hunger</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="thirst" class="bar-box">
        <div class="bar-head"><span id="labelThirst">Thirst</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="pain" class="bar-box">
        <div class="bar-head"><span id="labelPain">Pain</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="outOfBreath" class="bar-box">
        <div class="bar-head"><span id="labelOutOfBreath">Out of Breath</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
      <div id="queasy" class="bar-box">
        <div class="bar-head"><span id="labelQueasy">Queasy</span><span class="value">0%</span></div>
        <div class="bar-bg"><div class="bar-fill"></div></div>
      </div>
    </div>

    <div style="margin-top:8px;">
      <div class="muted" id="labelMoodles">Moodles</div>
      <div id="moodlesList" class="moodles-strip"></div>
    </div>
  </div>

  <script>
    const ids = {
      runId: document.getElementById("runId"),
      worldTime: document.getElementById("worldTime"),
      labelKillsTotal: document.getElementById("labelKillsTotal"),
      labelKillsThisSession: document.getElementById("labelKillsThisSession"),
      labelKillsPerHour: document.getElementById("labelKillsPerHour"),
      labelTimeSurvived: document.getElementById("labelTimeSurvived"),
      labelDanger: document.getElementById("labelDanger"),
      labelFatigue: document.getElementById("labelFatigue"),
      labelTiredness: document.getElementById("labelTiredness"),
      labelEndurance: document.getElementById("labelEndurance"),
      labelHunger: document.getElementById("labelHunger"),
      labelThirst: document.getElementById("labelThirst"),
      labelPain: document.getElementById("labelPain"),
      labelOutOfBreath: document.getElementById("labelOutOfBreath"),
      labelQueasy: document.getElementById("labelQueasy"),
      labelMoodles: document.getElementById("labelMoodles"),
      killsTotal: document.getElementById("killsTotal"),
      killsThisSession: document.getElementById("killsThisSession"),
      killsPerHour: document.getElementById("killsPerHour"),
      timeSurvived: document.getElementById("timeSurvived"),
      dangerPill: document.getElementById("dangerPill"),
      moodlesList: document.getElementById("moodlesList")
    };

    function toPercent(v) {
      const n = Number(v);
      if (!Number.isFinite(n)) return 0;
      return Math.max(0, Math.min(100, Math.round(n * 100)));
    }

    function setBar(key, value) {
      const box = document.getElementById(key);
      if (!box) return;
      const pct = toPercent(value);
      const fill = box.querySelector(".bar-fill");
      const text = box.querySelector(".value");
      fill.style.width = `${pct}%`;
      text.textContent = `${pct}%`;
    }

    function setDanger(labelKey, labelText, idx) {
      const up = (labelKey || "GRAY").toUpperCase();
      ids.dangerPill.textContent = `${labelText || up} (${idx ?? 0})`;
      ids.dangerPill.classList.remove("pill-green", "pill-yellow", "pill-orange", "pill-red");
      if (up === "RED") ids.dangerPill.classList.add("pill-red");
      else if (up === "ORANGE") ids.dangerPill.classList.add("pill-orange");
      else if (up === "YELLOW") ids.dangerPill.classList.add("pill-yellow");
      else ids.dangerPill.classList.add("pill-green");
    }

    function resolveMoodleIconFile(label) {
      const text = String(label || "").trim().toLowerCase();
      if (text.startsWith("hungry")) return "Status_Hunger.png";
      if (text.startsWith("thirsty")) return "Status_Thirst.png";
      if (text.startsWith("fatigue") || text.startsWith("tired")) return "Mood_Exhausted.png";
      if (text.startsWith("pain")) return "Mood_Pained.png";
      if (text.startsWith("out of breath")) return "Status_DifficultyBreathing.png";
      if (text.startsWith("panic")) return "Mood_Panicked.png";
      if (text.startsWith("stress")) return "Mood_Stressed.png";
      if (text.startsWith("queasy") || text.startsWith("nause")) return "Mood_Nauseous.png";
      if (text.startsWith("bad smell") || text.startsWith("noxious smell")) return "Mood_NoxiousSmell.png";
      if (text.startsWith("dead")) return "Mood_Dead.png";
      return "moodle_guide.png";
    }

    async function update() {
      try {
        const response = await fetch("/api/state", { cache: "no-store" });
        if (!response.ok) return;
        const data = await response.json();
        ids.runId.textContent = data.runId || "run-unknown";
        ids.worldTime.textContent = data.worldTime || "-";
        ids.labelKillsTotal.textContent = data.labelKillsTotal || "Kills Total";
        ids.labelKillsThisSession.textContent = data.labelKillsThisSession || "Kills This Session";
        ids.labelKillsPerHour.textContent = data.labelKillsPerHour || "Kills / Hour (played)";
        ids.labelTimeSurvived.textContent = data.labelTimeSurvived || "Time Survived";
        ids.labelDanger.textContent = data.labelDanger || "Danger Level";
        ids.labelFatigue.textContent = data.labelFatigue || "Fatigue";
        ids.labelTiredness.textContent = data.labelTiredness || "Tiredness";
        ids.labelEndurance.textContent = data.labelEndurance || "Endurance";
        ids.labelHunger.textContent = data.labelHunger || "Hunger";
        ids.labelThirst.textContent = data.labelThirst || "Thirst";
        ids.labelPain.textContent = data.labelPain || "Pain";
        ids.labelOutOfBreath.textContent = data.labelOutOfBreath || "Out of Breath";
        ids.labelQueasy.textContent = data.labelQueasy || "Queasy";
        ids.labelMoodles.textContent = data.labelMoodles || "Moodles";
        ids.killsTotal.textContent = String(data.killsTotal ?? 0);
        ids.killsThisSession.textContent = String(data.killsThisSession ?? 0);
        ids.killsPerHour.textContent = Number(data.killsPerHour ?? 0).toFixed(1);
        ids.timeSurvived.textContent = data.timeSurvived || "0d 00h 00m";
        setDanger(data.dangerLabel, data.dangerLabelText, data.dangerIndex);
        setBar("fatigue", data.fatigue);
        setBar("tiredness", data.tiredness);
        setBar("endurance", data.endurance);
        setBar("hunger", data.hunger);
        setBar("thirst", data.thirst);
        setBar("pain", data.pain);
        setBar("outOfBreath", data.outOfBreath);
        setBar("queasy", data.queasy);
        const moodles = Array.isArray(data.moodles)
          ? data.moodles.filter(m => typeof m === "string" && m.trim().length > 0)
          : [];
        ids.moodlesList.innerHTML = "";
        if (moodles.length > 0) {
          for (const moodle of moodles) {
            const icon = document.createElement("img");
            icon.className = "moodle-icon";
            icon.alt = moodle;
            icon.title = moodle;
            icon.src = `/api/moodle-icon/${encodeURIComponent(resolveMoodleIconFile(moodle))}`;
            ids.moodlesList.appendChild(icon);
          }
        }
      } catch {
        // Keep old values if polling fails.
      }
    }

    update();
    setInterval(update, 1000);
  </script>
</body>
</html>
""";
}
