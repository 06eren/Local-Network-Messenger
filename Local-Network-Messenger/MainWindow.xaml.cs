using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Local_Network_Messenger.Integrations;
using Local_Network_Messenger.Models;
using Local_Network_Messenger.Services;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace Local_Network_Messenger
{
    public partial class MainWindow : Window
    {
        private const string HostName = "app.local";
        private const long MaxPreviewBytes = 50 * 1024 * 1024;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        private readonly SessionState _sessionState;
        private readonly PasswordHasher _hasher;
        private readonly SessionStore _sessionStore;
        private IUserStore _userStore;
        private AuthService _authService;
        private AppConfig _config;
        private readonly ICryptoBridge _cryptoBridge;
        private readonly MessageCipher _messageCipher;
        private readonly ChatService _chatService;
        private IChatArchiveStore _chatArchiveStore;
        private SecurityEventLogger? _securityLogger;
        private readonly IFileScanService _fileScanService;
        private readonly LanTransportService _lanService;
        private readonly ISentimentService _sentimentService;
        private ProcessJsonClient? _cryptoClient;
        private Forms.NotifyIcon? _trayIcon;
        private bool _allowClose;
        private bool _trayHintShown;
        private readonly object _snapshotLock = new();
        private bool _snapshotScheduled;
        private string _activeThreadId = "all";
        private SentimentTone _lastTone = SentimentTone.Neutral;
        private bool _uiReady;
        private (string Message, string Tone, int AutoClearMs)? _pendingUiStatus;
        private FirewallEnsureResult? _firewallStatus;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _deliveryTimers = new();

        public MainWindow()
        {
            InitializeComponent();
            _sessionState = new SessionState();
            _hasher = new PasswordHasher();
            _sessionStore = new SessionStore(AppPaths.SessionPath);
            _userStore = new SqliteUserStore(AppPaths.UserDatabasePath);
            _securityLogger = new SecurityEventLogger(AppPaths.SecurityLogPath);
            _authService = new AuthService(_sessionState, _userStore, _hasher, _sessionStore, _securityLogger);
            _config = AppConfig.Load(AppPaths.ConfigPath);
            _cryptoBridge = CreateCryptoBridge(_config);
            _messageCipher = new MessageCipher(_cryptoBridge, _config.EffectiveCryptoKeyId);
            _chatArchiveStore = new NoopChatArchiveStore();
            _chatService = new ChatService(_sessionState, _messageCipher, _chatArchiveStore);
            _fileScanService = CreateFileScanService(_config);
            _lanService = new LanTransportService(
                _messageCipher,
                _fileScanService,
                _config.EffectiveDiscoveryPort,
                _config.EffectiveTcpPort);
            _sentimentService = SentimentServiceFactory.Create();
            _chatService.MessageReceived += OnMessageReceived;
            _lanService.PeerChanged += OnPeerChanged;
            _lanService.MessageReceived += OnLanMessageReceived;
            _lanService.FileReceived += OnLanFileReceived;
            _lanService.ChatAckReceived += OnLanChatAckReceived;
            _lanService.ChatReadReceived += OnLanChatReadReceived;
            _lanService.TypingReceived += OnLanTypingReceived;
            _lanService.FileTransferProgress += OnLanFileTransferProgress;
            _lanService.FileTransferStarted += OnLanFileTransferStarted;
            _lanService.ConnectionQualityUpdated += OnConnectionQualityUpdated;
            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            StateChanged += OnStateChanged;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "WebUI");
            if (!Directory.Exists(webRoot))
            {
                WpfMessageBox.Show($"WebUI klasoru bulunamadi: {webRoot}", "WebUI", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await InitializeUserStoreAsync();
            await InitializeArchiveStoreAsync();

            try
            {
                await MessengerView.EnsureCoreWebView2Async();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"WebView2 baslatilamadi: {ex.Message}", "WebView2", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var settings = MessengerView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            MessengerView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostName,
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);

            MessengerView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            MessengerView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            MessengerView.CoreWebView2.ContextMenuRequested += OnContextMenuRequested;
            MessengerView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MessengerView.Source = new Uri($"https://{HostName}/index.html");
            MessengerView.AllowDrop = true;
            MessengerView.PreviewDragOver += OnWebViewDragOver;
            MessengerView.Drop += OnWebViewDrop;

            InitializeTrayIcon();
            await EnsureFirewallRulesAsync();
        }

        private async Task InitializeUserStoreAsync()
        {
            try
            {
                await _userStore.InitializeAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Kalici veritabani baslatilamadi. Gecici bellek kullanilacak.\n{ex.Message}",
                    "Veritabani",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _userStore = new InMemoryUserStore();
                _authService = new AuthService(_sessionState, _userStore, _hasher, _sessionStore, _securityLogger);
            }
        }

        private async Task InitializeArchiveStoreAsync()
        {
            try
            {
                _chatArchiveStore = new SqliteChatArchiveStore(AppPaths.ChatDatabasePath);
                await _chatArchiveStore.InitializeAsync(CancellationToken.None);
                _chatService.SetArchiveStore(_chatArchiveStore);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Sohbet arsivi baslatilamadi. Gecici bellek kullanilacak.\n{ex.Message}",
                    "Arsiv",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                _chatArchiveStore = new NoopChatArchiveStore();
                _chatService.SetArchiveStore(_chatArchiveStore);
            }
        }

        private async Task EnsureFirewallRulesAsync()
        {
            var firewall = new FirewallService();
            var result = await firewall.EnsureAsync(CancellationToken.None);
            _firewallStatus = result;
            if (result.Success)
            {
                QueueUiStatus("Guvenlik kurallari hazir.", "info", 3500);
                return;
            }

            QueueUiStatus(result.Message, "error", 6000);
            WpfMessageBox.Show(
                $"Firewall kurallari ayarlanamadi.\n{result.Message}",
                "Guvenlik",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private ICryptoBridge CreateCryptoBridge(AppConfig config)
        {
            if (!string.IsNullOrWhiteSpace(config.CryptoDllPath) && File.Exists(config.CryptoDllPath))
            {
                try
                {
                    return new CryptoDllBridge(config.CryptoDllPath);
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(
                        $"C++ DLL sifreleme yuklenemedi. Proses modu denenecek.\n{ex.Message}",
                        "Sifreleme",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            if (string.IsNullOrWhiteSpace(config.CryptoExecutable))
            {
                return new PassThroughCryptoBridge();
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = config.CryptoExecutable,
                    Arguments = config.CryptoArguments ?? string.Empty
                };
                _cryptoClient = new ProcessJsonClient(startInfo, _jsonOptions, TimeSpan.FromSeconds(6));
                return new CryptoProcessBridge(_cryptoClient);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"C++ sifreleme servisi baslatilamadi. Gecici mod kullanilacak.\n{ex.Message}",
                    "Sifreleme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return new PassThroughCryptoBridge();
            }
        }

        private IFileScanService CreateFileScanService(AppConfig config)
        {
            if (PythonNetRuntime.TryInitialize(out var pythonError))
            {
                try
                {
                    return new PythonNetFileScanService();
                }
                catch (Exception ex)
                {
                    WpfMessageBox.Show(
                        $"Python.NET tarama servisi baslatilamadi. Proses modu denenecek.\n{ex.Message}",
                        "Tarama",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            if (string.IsNullOrWhiteSpace(config.ScanExecutable))
            {
                return new FileScanService();
            }

            try
            {
                ProcessJsonClient BuildClient()
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = config.ScanExecutable!,
                        Arguments = NormalizePythonScanArguments(config.ScanArguments)
                    };
                    startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                    startInfo.Environment["PYTHONUTF8"] = "1";
                    startInfo.Environment["PYTHONUNBUFFERED"] = "1";
                    return new ProcessJsonClient(startInfo, _jsonOptions, TimeSpan.FromSeconds(8));
                }

                var client = BuildClient();
                return new FileScanProcessService(client, BuildClient);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    $"Python tarama servisi baslatilamadi. Gecici mod kullanilacak.\n{ex.Message}",
                    "Tarama",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return new FileScanService();
            }
        }

        private void InitializeTrayIcon()
        {
            if (_trayIcon != null)
            {
                return;
            }

            _trayIcon = new Forms.NotifyIcon
            {
                Text = "Yerel Ag Mesajlasma",
                Icon = SystemIcons.Application,
                Visible = true
            };

            var menu = new Forms.ContextMenuStrip();
            var openItem = new Forms.ToolStripMenuItem("Ac");
            openItem.Click += (_, _) => ShowFromTray();
            var exitItem = new Forms.ToolStripMenuItem("Cikis");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => ShowFromTray();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                HideToTray();
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_allowClose)
            {
                return;
            }

            e.Cancel = true;
            HideToTray();
        }

        private async void OnClosed(object? sender, EventArgs e)
        {
            _chatService.MessageReceived -= OnMessageReceived;
            _lanService.PeerChanged -= OnPeerChanged;
            _lanService.MessageReceived -= OnLanMessageReceived;
            _lanService.FileReceived -= OnLanFileReceived;
            _lanService.ChatAckReceived -= OnLanChatAckReceived;
            _lanService.ChatReadReceived -= OnLanChatReadReceived;
            _lanService.TypingReceived -= OnLanTypingReceived;
            _lanService.FileTransferProgress -= OnLanFileTransferProgress;
            _lanService.FileTransferStarted -= OnLanFileTransferStarted;
            _lanService.ConnectionQualityUpdated -= OnConnectionQualityUpdated;
            await _lanService.DisposeAsync();
            PythonNetRuntime.Shutdown();
            if (_cryptoBridge is IDisposable disposableBridge)
            {
                disposableBridge.Dispose();
            }
            _trayIcon?.Dispose();
            _trayIcon = null;
            if (_cryptoClient != null)
            {
                await _cryptoClient.DisposeAsync();
                _cryptoClient = null;
            }

            if (_fileScanService is IAsyncDisposable scanDisposable)
            {
                await scanDisposable.DisposeAsync();
            }
        }

        private void HideToTray()
        {
            if (_trayIcon == null)
            {
                return;
            }

            ShowInTaskbar = false;
            Hide();
            if (!_trayHintShown)
            {
                _trayIcon.ShowBalloonTip(
                    1500,
                    "Yerel Ag Mesajlasma",
                    "Uygulama arka planda calisiyor.",
                    Forms.ToolTipIcon.Info);
                _trayHintShown = true;
            }
        }

        private void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        internal void BringToFrontFromExternal()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(BringToFrontFromExternal);
                return;
            }

            ShowFromTray();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void ExitApplication()
        {
            _allowClose = true;
            _trayIcon?.Visible = false;
            Close();
        }

        private void OnMessageReceived(object? sender, ChatService.ChatNotification notification)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_trayIcon == null)
                {
                    return;
                }

                if (IsVisible && WindowState != WindowState.Minimized)
                {
                    return;
                }

                var title = notification.DisplayName;
                var text = $"{notification.Sender}: {notification.Text}";
                _trayIcon.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.Info);
            });
        }

        private void OnPeerChanged(object? sender, PeerChangedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _chatService.UpdateContactPresence(
                    e.Peer.Username,
                    e.Peer.DisplayName,
                    e.Peer.IsOnline,
                    e.Peer.LastSeen);
                ScheduleSnapshotPush();
            });
        }

        private void OnConnectionQualityUpdated(object? sender, ConnectionQualityEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _chatService.UpdateConnectionQuality(e.Username, e.PingMs, e.LossPercent);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanMessageReceived(object? sender, LanMessageReceivedEventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await _chatService.AddIncomingMessageAsync(
                    e.ThreadId,
                    e.FromDisplayName,
                    e.Text,
                    CancellationToken.None);
                if (string.Equals(e.ThreadId, _activeThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    _chatService.MarkThreadAsRead(e.ThreadId);
                    _ = _lanService.SendReadReceiptAsync(e.ThreadId, e.ThreadId, CancellationToken.None);
                }
                await ApplySentimentThemeAsync(e.Text);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanFileReceived(object? sender, LanFileReceivedEventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                var preview = await TryBuildImagePreviewFromFileAsync(
                    e.FilePath,
                    e.ContentType,
                    e.SizeBytes);
                await _chatService.AddIncomingFileMessageAsync(
                    e.ThreadId,
                    e.FromDisplayName,
                    e.FileName,
                    e.SizeBytes,
                    e.ScanResult.Status,
                    CancellationToken.None,
                    e.MessageId,
                    e.ContentType,
                    preview,
                    e.FilePath);
                _chatService.UpdateFileProgress(e.MessageId, 100, "completed");
                if (string.Equals(e.ThreadId, _activeThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    _chatService.MarkThreadAsRead(e.ThreadId);
                    _ = _lanService.SendReadReceiptAsync(e.ThreadId, e.ThreadId, CancellationToken.None);
                }
                ScheduleSnapshotPush();
                var tone = string.Equals(e.ScanResult.Status, "clean", StringComparison.OrdinalIgnoreCase)
                    ? "info"
                    : "error";
                await SendChatStatusAsync(e.ScanResult.Message, tone);
            });
        }

        private void OnLanChatAckReceived(object? sender, LanChatAckReceivedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                ClearDeliveryTimeout(e.MessageId);
                _chatService.UpdateDeliveryState(e.MessageId, e.Status);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanChatReadReceived(object? sender, LanChatReadReceivedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _chatService.MarkThreadReadByPeer(e.ThreadId);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanTypingReceived(object? sender, LanTypingReceivedEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _chatService.UpdateTypingStatus(e.ThreadId, e.IsTyping);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanFileTransferProgress(object? sender, FileTransferProgressEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var state = e.IsOutgoing ? "sending" : "receiving";
                _chatService.UpdateFileProgress(e.MessageId, e.Progress, state);
                ScheduleSnapshotPush();
            });
        }

        private void OnLanFileTransferStarted(object? sender, FileTransferStartedEventArgs e)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await _chatService.AddIncomingFileMessageAsync(
                    e.ThreadId,
                    e.FromDisplayName,
                    e.FileName,
                    e.SizeBytes,
                    "pending",
                    CancellationToken.None,
                    e.MessageId);
                ScheduleSnapshotPush();
            });
        }

        private async Task StartLanAsync(UserProfile user)
        {
            await _lanService.StartAsync(user, CancellationToken.None);
            _activeThreadId = "all";
            await ApplyManualPeersAsync();
            await ConfigureRelayAsync();
            ScheduleSnapshotPush();
        }

        private async Task StopLanAsync()
        {
            await _lanService.StopAsync();
            _chatService.Reset();
        }

        private async Task ApplyManualPeersAsync()
        {
            foreach (var endpoint in _config.EffectiveManualPeers)
            {
                if (!TryParseEndpoint(endpoint, out var host, out var port))
                {
                    continue;
                }

                if (port <= 0)
                {
                    port = _config.EffectiveTcpPort;
                }

                await _lanService.AddManualPeerAsync(host, port, null, CancellationToken.None);
            }
        }

        private void ScheduleSnapshotPush()
        {
            lock (_snapshotLock)
            {
                if (_snapshotScheduled)
                {
                    return;
                }
                _snapshotScheduled = true;
            }

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(200);
                await SendChatPushAsync();
                lock (_snapshotLock)
                {
                    _snapshotScheduled = false;
                }
            });
        }

        private async Task SendChatPushAsync()
        {
            if (!_sessionState.IsAuthenticated)
            {
                return;
            }

            if (MessengerView.CoreWebView2 == null)
            {
                return;
            }

            var snapshot = await _chatService.GetSnapshotAsync(null, CancellationToken.None);
            var payload = new
            {
                currentUser = snapshot.CurrentUser,
                contacts = snapshot.Contacts,
                threads = snapshot.Threads,
                activeContactId = _activeThreadId
            };
            var response = new WebResponse(Guid.NewGuid().ToString("N"), "chat.push", true, payload, null);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private Task SendChatStatusAsync(string message, string tone)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.InvokeAsync(() => SendChatStatusAsync(message, tone)).Task;
            }

            if (MessengerView.CoreWebView2 == null)
            {
                return Task.CompletedTask;
            }

            var payload = new { message, tone };
            var response = new WebResponse(Guid.NewGuid().ToString("N"), "chat.status", true, payload, null);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }

        private Task SendUiStatusAsync(string message, string tone, int autoClearMs)
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.InvokeAsync(() => SendUiStatusAsync(message, tone, autoClearMs)).Task;
            }

            if (MessengerView.CoreWebView2 == null)
            {
                return Task.CompletedTask;
            }

            var payload = new { message, tone, autoClearMs };
            var response = new WebResponse(Guid.NewGuid().ToString("N"), "ui.status", true, payload, null);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }

        private void QueueUiStatus(string message, string tone, int autoClearMs)
        {
            _pendingUiStatus = (message, tone, autoClearMs);
            if (_uiReady)
            {
                _ = SendUiStatusAsync(message, tone, autoClearMs);
            }
        }

        private async Task HandleDroppedFileAsync(string threadId, string filePath)
        {
            if (!File.Exists(filePath))
            {
                await SendChatStatusAsync("Dosya bulunamadi.", "error");
                return;
            }

            var info = new FileInfo(filePath);
            var contentType = GetContentType(info.Extension);
            var scan = await _fileScanService.ScanAsync(
                new FileScanRequest(info.Name, info.Length, contentType),
                CancellationToken.None);
            var preview = await TryBuildImagePreviewFromFileAsync(filePath, contentType, info.Length);

            ChatMessageDto messageDto;
            try
            {
                messageDto = await _chatService.AddFileMessageAsync(
                    threadId,
                    info.Name,
                    info.Length,
                    scan.Status,
                    CancellationToken.None,
                    null,
                    contentType,
                    preview,
                    null);
            }
            catch (InvalidOperationException ex)
            {
                await SendChatStatusAsync(ex.Message, "error");
                return;
            }

            ScheduleSnapshotPush();
            var tone = string.Equals(scan.Status, "clean", StringComparison.OrdinalIgnoreCase) ? "info" : "error";
            await SendChatStatusAsync(scan.Message, tone);
            _ = SendNetworkFileFromPathAsync(threadId, filePath, contentType, messageDto.Id);
        }

        private Task SendUiDropAsync()
        {
            if (MessengerView.CoreWebView2 == null)
            {
                return Task.CompletedTask;
            }

            var response = new WebResponse(Guid.NewGuid().ToString("N"), "ui.drop", true, new { action = "hide" }, null);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }

        private void TrackDeliveryTimeout(string messageId, string threadId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            if (string.Equals(threadId, "all", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _deliveryTimers.AddOrUpdate(
                messageId,
                _ => cts,
                (_, existing) =>
                {
                    existing.Cancel();
                    existing.Dispose();
                    return cts;
                });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(8), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (cts.IsCancellationRequested)
                {
                    return;
                }

                _chatService.UpdateDeliveryState(messageId, "failed");
                ScheduleSnapshotPush();
            });
        }

        private void ClearDeliveryTimeout(string messageId)
        {
            if (_deliveryTimers.TryRemove(messageId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        private async Task ApplySentimentThemeAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                var result = await _sentimentService.AnalyzeAsync(text, CancellationToken.None);
                if (result.Tone == _lastTone)
                {
                    return;
                }

                _lastTone = result.Tone;
                var palette = ThemeFromTone(result.Tone);
                await SendThemeAsync(palette);
            }
            catch (Exception)
            {
            }
        }

        private Task SendThemeAsync(ThemePalette palette)
        {
            if (MessengerView.CoreWebView2 == null)
            {
                return Task.CompletedTask;
            }

            var payload = new
            {
                accent = palette.Accent,
                accentStrong = palette.AccentStrong,
                accentSoft = palette.AccentSoft,
                bubbleMine = palette.BubbleMine
            };
            var response = new WebResponse(Guid.NewGuid().ToString("N"), "ui.theme", true, payload, null);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }

        private static ThemePalette ThemeFromTone(SentimentTone tone)
        {
            return tone switch
            {
                SentimentTone.Positive => new ThemePalette(
                    "#2F9C7A",
                    "#247861",
                    "rgba(47, 156, 122, 0.25)",
                    "#1C3F33"),
                SentimentTone.Negative => new ThemePalette(
                    "#8A4A4F",
                    "#6C3A3E",
                    "rgba(138, 74, 79, 0.2)",
                    "#3A2628"),
                _ => new ThemePalette(
                    "#3C8C7E",
                    "#2F7267",
                    "rgba(60, 140, 126, 0.2)",
                    "#1E3F38")
            };
        }

        private async Task SendNetworkMessageAsync(string threadId, string text, string messageId)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                var result = await _lanService.SendMessageAsync(threadId, text, messageId, CancellationToken.None);
                if (!result.Success)
                {
                    ClearDeliveryTimeout(messageId);
                    _chatService.UpdateDeliveryState(messageId, "failed");
                    ScheduleSnapshotPush();
                    await SendChatStatusAsync(result.Message, "error");
                }
            }
            catch (Exception ex)
            {
                ClearDeliveryTimeout(messageId);
                _chatService.UpdateDeliveryState(messageId, "failed");
                ScheduleSnapshotPush();
                await SendChatStatusAsync($"Ag gonderimi basarisiz: {ex.Message}", "error");
            }
        }

        private async Task SendNetworkFileAsync(string threadId, string fileName, byte[] data, string? contentType, string messageId)
        {
            try
            {
                var result = await _lanService.SendFileAsync(threadId, fileName, data, contentType, messageId, CancellationToken.None);
                if (!result.Success)
                {
                    _chatService.UpdateFileStatus(messageId, "error");
                    _chatService.UpdateFileProgress(messageId, 0, "failed");
                    ScheduleSnapshotPush();
                    await SendChatStatusAsync(result.Message, "error");
                }
            }
            catch (Exception ex)
            {
                _chatService.UpdateFileStatus(messageId, "error");
                _chatService.UpdateFileProgress(messageId, 0, "failed");
                ScheduleSnapshotPush();
                await SendChatStatusAsync($"Dosya gonderimi basarisiz: {ex.Message}", "error");
            }
        }

        private async Task SendNetworkFileFromPathAsync(string threadId, string filePath, string? contentType, string messageId)
        {
            try
            {
                var result = await _lanService.SendFileFromPathAsync(threadId, filePath, contentType, messageId, CancellationToken.None);
                if (!result.Success)
                {
                    _chatService.UpdateFileStatus(messageId, "error");
                    _chatService.UpdateFileProgress(messageId, 0, "failed");
                    ScheduleSnapshotPush();
                    await SendChatStatusAsync(result.Message, "error");
                }
            }
            catch (Exception ex)
            {
                _chatService.UpdateFileStatus(messageId, "error");
                _chatService.UpdateFileProgress(messageId, 0, "failed");
                ScheduleSnapshotPush();
                await SendChatStatusAsync($"Dosya gonderimi basarisiz: {ex.Message}", "error");
            }
        }

        private static string GetContentType(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return "application/octet-stream";
            }

            return extension.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".tif" => "image/tiff",
                ".tiff" => "image/tiff",
                ".heic" => "image/heic",
                ".heif" => "image/heif",
                ".svg" => "image/svg+xml",
                ".mp4" => "video/mp4",
                ".m4v" => "video/x-m4v",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".avi" => "video/x-msvideo",
                ".wmv" => "video/x-ms-wmv",
                ".mkv" => "video/x-matroska",
                ".flv" => "video/x-flv",
                ".3gp" => "video/3gpp",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".flac" => "audio/flac",
                ".aac" => "audio/aac",
                ".m4a" => "audio/mp4",
                ".ogg" => "audio/ogg",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".zip" => "application/zip",
                ".rar" => "application/vnd.rar",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                _ => "application/octet-stream"
            };
        }

        private static bool IsPreviewableFile(string fileName, string? contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType) &&
                (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                 contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
                 contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff" or ".heic" or ".heif" or ".svg"
                or ".mp4" or ".m4v" or ".mov" or ".webm" or ".avi" or ".wmv" or ".mkv" or ".flv" or ".3gp"
                or ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg";
        }

        private static string? TryBuildImagePreviewFromBase64(
            string fileName,
            string? contentType,
            long sizeBytes,
            string base64)
        {
            if (!IsPreviewableFile(fileName, contentType))
            {
                return null;
            }

            if (sizeBytes <= 0 || sizeBytes > MaxPreviewBytes)
            {
                return null;
            }

            var type = string.IsNullOrWhiteSpace(contentType)
                ? GetContentType(Path.GetExtension(fileName))
                : contentType;
            return $"data:{type};base64,{base64}";
        }

        private static async Task<string?> TryBuildImagePreviewFromFileAsync(
            string filePath,
            string? contentType,
            long sizeBytes)
        {
            if (!IsPreviewableFile(filePath, contentType))
            {
                return null;
            }

            if (sizeBytes <= 0 || sizeBytes > MaxPreviewBytes)
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(filePath);
            }
            catch (IOException)
            {
                return null;
            }

            if (bytes.Length == 0)
            {
                return null;
            }

            var type = string.IsNullOrWhiteSpace(contentType)
                ? GetContentType(Path.GetExtension(filePath))
                : contentType;
            return $"data:{type};base64,{Convert.ToBase64String(bytes)}";
        }

        private static string NormalizeNetworkKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var bytes = Encoding.UTF8.GetBytes(key.Trim());
            var hash = SHA256.HashData(bytes);
            return $"nk-{Convert.ToHexString(hash).ToLowerInvariant()}";
        }

        private static string NormalizePythonScanArguments(string? args)
        {
            var normalized = args ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                var script = Path.Combine(AppContext.BaseDirectory, "Tools", "scan_service.py");
                if (File.Exists(script))
                {
                    return $"-u -X utf8 \"{script}\"";
                }

                return string.Empty;
            }

            if (normalized.Contains("-u", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("-X utf8", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return $"-u -X utf8 {normalized}";
        }

        private static bool TryParseEndpoint(string value, out string host, out int port)
        {
            host = string.Empty;
            port = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            var colonIndex = trimmed.LastIndexOf(':');
            if (colonIndex > 0)
            {
                host = trimmed.Substring(0, colonIndex);
                var portText = trimmed.Substring(colonIndex + 1);
                if (!int.TryParse(portText, out port))
                {
                    return false;
                }
                return true;
            }

            host = trimmed;
            port = 0;
            return true;
        }

        private async Task ConfigureRelayAsync()
        {
            await _lanService.ConfigureRelayAsync(
                _config,
                _sessionState.CurrentUser,
                CancellationToken.None);
        }

        private object BuildDiagnosticsSnapshot()
        {
            var cryptoMode = _cryptoBridge switch
            {
                CryptoDllBridge => "dll",
                CryptoProcessBridge => "process",
                PassThroughCryptoBridge => "disabled",
                _ => "unknown"
            };

            var scanMode = _fileScanService switch
            {
                PythonNetFileScanService => "pythonnet",
                FileScanProcessService => "process",
                _ => "basic"
            };

            return new
            {
                firewall = new
                {
                    ok = _firewallStatus?.Success ?? false,
                    message = _firewallStatus?.Message ?? "Durum bilinmiyor."
                },
                ports = new
                {
                    discovery = _config.EffectiveDiscoveryPort,
                    tcp = _lanService.ListenPort
                },
                crypto = new
                {
                    mode = cryptoMode,
                    keyId = _config.EffectiveCryptoKeyId,
                    status = _cryptoBridge is PassThroughCryptoBridge ? "pasif" : "aktif"
                },
                scan = new
                {
                    mode = scanMode,
                    status = _fileScanService is FileScanService ? "pasif" : "aktif"
                },
                relay = new
                {
                    enabled = _config.EffectiveRelayEnabled,
                    host = _config.RelayHost ?? string.Empty,
                    mode = _config.EffectiveRelayMode,
                    serverEnabled = _config.RelayServerEnabled ?? false,
                    port = _config.EffectiveRelayPort,
                    serverPort = _config.EffectiveRelayServerPort,
                    connected = _lanService.IsRelayConnected
                },
                manualPeers = _lanService.ManualPeerCount
            };
        }

        private async Task<IReadOnlyList<SecurityLogEntry>> ReadSecurityLogsAsync(int limit)
        {
            if (!File.Exists(AppPaths.SecurityLogPath))
            {
                return Array.Empty<SecurityLogEntry>();
            }

            string[] lines;
            try
            {
                lines = await File.ReadAllLinesAsync(AppPaths.SecurityLogPath);
            }
            catch (IOException)
            {
                return Array.Empty<SecurityLogEntry>();
            }

            if (lines.Length == 0)
            {
                return Array.Empty<SecurityLogEntry>();
            }

            var start = Math.Max(0, lines.Length - limit);
            var entries = new List<SecurityLogEntry>();
            for (var i = start; i < lines.Length; i += 1)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<SecurityLogEntry>(line, _jsonOptions);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                }
            }

            return entries;
        }

        private async Task<List<ArchiveExportEntry>> BuildArchiveExportAsync(
            IReadOnlyList<ChatArchiveRecord> records,
            CancellationToken cancellationToken)
        {
            var entries = new List<ArchiveExportEntry>(records.Count);
            foreach (var record in records)
            {
                var text = await _messageCipher.DecryptAsync(record.TextCipher, cancellationToken);
                var fileName = record.AttachmentFileNameCipher == null
                    ? null
                    : await _messageCipher.DecryptAsync(record.AttachmentFileNameCipher, cancellationToken);
                var sentAt = record.SentAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                entries.Add(new ArchiveExportEntry(
                    record.ThreadId,
                    record.Sender,
                    record.IsMine,
                    text,
                    sentAt,
                    record.DeliveryState,
                    fileName,
                    record.AttachmentSizeBytes,
                    record.AttachmentStatus,
                    record.AttachmentProgress,
                    record.AttachmentTransferState));
            }

            return entries;
        }

        private static string BuildCsv(IReadOnlyList<ArchiveExportEntry> entries)
        {
            var builder = new StringBuilder();
            builder.AppendLine("SentAt,ThreadId,Sender,IsMine,Text,DeliveryState,AttachmentFileName,AttachmentSizeBytes,AttachmentStatus,AttachmentProgress,AttachmentTransferState");
            foreach (var entry in entries)
            {
                builder.Append(EscapeCsv(entry.SentAt)).Append(',')
                    .Append(EscapeCsv(entry.ThreadId)).Append(',')
                    .Append(EscapeCsv(entry.Sender)).Append(',')
                    .Append(EscapeCsv(entry.IsMine ? "true" : "false")).Append(',')
                    .Append(EscapeCsv(entry.Text)).Append(',')
                    .Append(EscapeCsv(entry.DeliveryState)).Append(',')
                    .Append(EscapeCsv(entry.AttachmentFileName)).Append(',')
                    .Append(EscapeCsv(entry.AttachmentSizeBytes?.ToString())).Append(',')
                    .Append(EscapeCsv(entry.AttachmentStatus)).Append(',')
                    .Append(EscapeCsv(entry.AttachmentProgress?.ToString("0.##"))).Append(',')
                    .Append(EscapeCsv(entry.AttachmentTransferState)).AppendLine();
            }

            return builder.ToString();
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var escaped = value.Replace("\"", "\"\"");
            if (escaped.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            {
                return $"\"{escaped}\"";
            }

            return escaped;
        }

        private sealed record ThemePalette(string Accent, string AccentStrong, string AccentSoft, string BubbleMine);
        private sealed record SecurityLogEntry(DateTimeOffset At, string Type, string? User, string Message, string? Details);
        private sealed record ArchiveExportEntry(
            string ThreadId,
            string Sender,
            bool IsMine,
            string Text,
            string SentAt,
            string? DeliveryState,
            string? AttachmentFileName,
            long? AttachmentSizeBytes,
            string? AttachmentStatus,
            double? AttachmentProgress,
            string? AttachmentTransferState);

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!e.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _uiReady = true;
            if (_pendingUiStatus.HasValue)
            {
                var pending = _pendingUiStatus.Value;
                _pendingUiStatus = null;
                _ = SendUiStatusAsync(pending.Message, pending.Tone, pending.AutoClearMs);
            }
        }

        private void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void OnWebViewDragOver(object? sender, System.Windows.DragEventArgs e)
        {
            if (!_sessionState.IsAuthenticated)
            {
                e.Effects = System.Windows.DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }

            e.Handled = true;
        }

        private async void OnWebViewDrop(object? sender, System.Windows.DragEventArgs e)
        {
            e.Handled = true;
            if (!_sessionState.IsAuthenticated)
            {
                await SendChatStatusAsync("Oturum bulunamadi.", "error");
                return;
            }

            var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0)
            {
                await SendChatStatusAsync("Dosya bulunamadi.", "error");
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeThreadId))
            {
                await SendChatStatusAsync("Sohbet secmeden dosya gonderemezsin.", "error");
                return;
            }

            await HandleDroppedFileAsync(_activeThreadId, files[0]);
            await SendUiDropAsync();
        }

        private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            WebMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<WebMessage>(e.WebMessageAsJson, _jsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (message == null)
            {
                return;
            }

            switch (message.Type)
            {
                case "auth.login":
                {
                    var request = DeserializePayload<LoginRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Login verisi okunamadi.");
                        return;
                    }

                    var result = await _authService.LoginAsync(request, CancellationToken.None);
                    await SendResponseAsync(
                        message,
                        "auth.result",
                        result.Success,
                        new { user = result.User, message = result.Message },
                        result.Errors);
                    if (result.Success && result.User != null)
                    {
                        await _chatService.LoadHistoryAsync(result.User, CancellationToken.None);
                        await StartLanAsync(result.User);
                    }
                    return;
                }
                case "auth.register":
                {
                    var request = DeserializePayload<RegisterRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Kayit verisi okunamadi.");
                        return;
                    }

                    var result = await _authService.RegisterAsync(request, CancellationToken.None);
                    await SendResponseAsync(
                        message,
                        "auth.result",
                        result.Success,
                        new { user = result.User, message = result.Message },
                        result.Errors);
                    if (result.Success && result.User != null)
                    {
                        await _chatService.LoadHistoryAsync(result.User, CancellationToken.None);
                        await StartLanAsync(result.User);
                    }
                    return;
                }
                case "auth.rename":
                {
                    var request = DeserializePayload<RenameRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Kullanici adi verisi okunamadi.");
                        return;
                    }

                    var result = await _authService.RenameAsync(request, CancellationToken.None);
                    await SendResponseAsync(
                        message,
                        "auth.rename",
                        result.Success,
                        new { user = result.User, message = result.Message },
                        result.Errors);
                    if (result.Success && result.User != null)
                    {
                        await StartLanAsync(result.User);
                    }
                    return;
                }
                case "auth.displayName":
                {
                    var request = DeserializePayload<DisplayNameRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Gorunen ad verisi okunamadi.");
                        return;
                    }

                    var result = await _authService.UpdateDisplayNameAsync(request, CancellationToken.None);
                    await SendResponseAsync(
                        message,
                        "auth.displayName",
                        result.Success,
                        new { user = result.User, message = result.Message },
                        result.Errors);
                    if (result.Success && result.User != null)
                    {
                        await StartLanAsync(result.User);
                    }
                    return;
                }
                case "auth.logout":
                {
                    await StopLanAsync();
                    _activeThreadId = string.Empty;
                    var result = await _authService.LogoutAsync(CancellationToken.None);
                    await SendResponseAsync(
                        message,
                        "auth.logout",
                        result.Success,
                        new { message = result.Message },
                        result.Errors);
                    return;
                }
                case "auth.restore":
                {
                    var user = await _authService.RestoreSessionAsync(CancellationToken.None);
                    if (user == null)
                    {
                        await SendResponseAsync(
                            message,
                            "auth.restore",
                            false,
                            new { message = "Oturum bulunamadi." },
                            null);
                        return;
                    }

                    await SendResponseAsync(
                        message,
                        "auth.restore",
                        true,
                        new { user, message = "Oturum yuklendi." },
                        null);
                    await _chatService.LoadHistoryAsync(user, CancellationToken.None);
                    await StartLanAsync(user);
                    return;
                }
                case "chat.active":
                {
                    var request = DeserializePayload<ChatActiveRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.ThreadId))
                    {
                        await SendErrorAsync(message, "Sohbet bilgisi okunamadi.");
                        return;
                    }

                    _activeThreadId = request.ThreadId;
                    _chatService.MarkThreadAsRead(request.ThreadId);
                    if (!string.Equals(request.ThreadId, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = _lanService.SendReadReceiptAsync(request.ThreadId, request.ThreadId, CancellationToken.None);
                    }
                    ScheduleSnapshotPush();
                    await SendResponseAsync(message, "chat.active", true, new { message = "Guncellendi." }, null);
                    return;
                }
                case "chat.typing":
                {
                    var request = DeserializePayload<ChatTypingRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.ThreadId))
                    {
                        await SendErrorAsync(message, "Yaziyor bilgisi okunamadi.");
                        return;
                    }

                    await _lanService.SendTypingAsync(request.ThreadId, request.ThreadId, request.IsTyping, CancellationToken.None);
                    await SendResponseAsync(message, "chat.typing", true, new { message = "Guncellendi." }, null);
                    return;
                }
                case "chat.snapshot":
                {
                    if (!_sessionState.IsAuthenticated)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var snapshot = await _chatService.GetSnapshotAsync(null, CancellationToken.None);
                    await SendResponseAsync(message, "chat.snapshot", true, snapshot, null);
                    return;
                }
                case "chat.send":
                {
                    if (!_sessionState.IsAuthenticated)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var request = DeserializePayload<ChatSendRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Mesaj verisi okunamadi.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(request.Text))
                    {
                        await SendResponseAsync(
                            message,
                            "chat.send",
                            false,
                            new { message = "Bos mesaj gonderilemez." },
                            new List<ValidationError> { new("message", "Bos mesaj gonderilemez.") });
                        return;
                    }

                    ChatMessageDto messageDto;
                    try
                    {
                        messageDto = await _chatService.AddMessageAsync(
                            request.ThreadId,
                            request.Text.Trim(),
                            CancellationToken.None);
                    }
                    catch (InvalidOperationException ex)
                    {
                        await SendErrorAsync(message, ex.Message);
                        return;
                    }

                    await SendResponseAsync(message, "chat.send", true, new { message = messageDto }, null);
                    TrackDeliveryTimeout(messageDto.Id, request.ThreadId);
                    _ = SendNetworkMessageAsync(request.ThreadId, request.Text.Trim(), messageDto.Id);
                    _ = ApplySentimentThemeAsync(request.Text.Trim());
                    return;
                }
                case "settings.networkKey":
                {
                    var request = DeserializePayload<NetworkKeyRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Anahtar verisi okunamadi.");
                        return;
                    }

                    var normalized = NormalizeNetworkKey(request.NetworkKey);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        await SendErrorAsync(message, "Ag anahtari bos olamaz.");
                        return;
                    }

                    _config = _config with { CryptoKeyId = normalized };
                    AppConfig.Save(AppPaths.ConfigPath, _config);
                    _messageCipher.UpdateKeyId(_config.EffectiveCryptoKeyId);
                    QueueUiStatus("Ag anahtari guncellendi. Eski mesajlar okunamayabilir.", "info", 6000);
                    await SendResponseAsync(message, "settings.networkKey", true, new { message = "Ag anahtari kaydedildi." }, null);
                    return;
                }
                case "net.manualPeer":
                {
                    var request = DeserializePayload<ManualPeerRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.Endpoint))
                    {
                        await SendErrorAsync(message, "Manuel IP okunamadi.");
                        return;
                    }

                    if (!TryParseEndpoint(request.Endpoint, out var host, out var port))
                    {
                        await SendErrorAsync(message, "Manuel IP formati hatali.");
                        return;
                    }

                    if (port <= 0)
                    {
                        port = _config.EffectiveTcpPort;
                    }

                    var peer = await _lanService.AddManualPeerAsync(host, port, null, CancellationToken.None);
                    if (peer == null)
                    {
                        await SendErrorAsync(message, "Baglanti kurulamadi.");
                        return;
                    }

                    var manualPeers = new List<string>(_config.EffectiveManualPeers);
                    if (!manualPeers.Exists(item => string.Equals(item, request.Endpoint, StringComparison.OrdinalIgnoreCase)))
                    {
                        manualPeers.Add(request.Endpoint);
                        _config = _config with { ManualPeers = manualPeers.ToArray() };
                        AppConfig.Save(AppPaths.ConfigPath, _config);
                    }

                    ScheduleSnapshotPush();
                    await SendResponseAsync(message, "net.manualPeer", true, new { message = "Manuel baglanti eklendi." }, null);
                    return;
                }
                case "relay.config":
                {
                    var request = DeserializePayload<RelayConfigRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Relay verisi okunamadi.");
                        return;
                    }

                    _config = _config with
                    {
                        RelayHost = request.Host,
                        RelayPort = request.RelayPort,
                        RelayMode = request.Mode,
                        RelayEnabled = request.RelayEnabled,
                        RelayServerEnabled = request.RelayServerEnabled,
                        RelayServerPort = request.RelayServerPort
                    };
                    AppConfig.Save(AppPaths.ConfigPath, _config);
                    await ConfigureRelayAsync();
                    await SendResponseAsync(message, "relay.config", true, new { message = "Relay ayari guncellendi." }, null);
                    return;
                }
                case "diag.snapshot":
                {
                    var payload = BuildDiagnosticsSnapshot();
                    await SendResponseAsync(message, "diag.snapshot", true, payload, null);
                    return;
                }
                case "logs.security":
                {
                    var request = DeserializePayload<LogReadRequest>(message.Payload);
                    var limit = request?.Limit ?? 80;
                    if (limit < 1)
                    {
                        limit = 1;
                    }
                    if (limit > 300)
                    {
                        limit = 300;
                    }

                    var entries = await ReadSecurityLogsAsync(limit);
                    await SendResponseAsync(message, "logs.security", true, new { entries }, null);
                    return;
                }
                case "logs.download":
                {
                    if (!File.Exists(AppPaths.SecurityLogPath))
                    {
                        await SendErrorAsync(message, "Guvenlik logu bulunamadi.");
                        return;
                    }

                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Guvenlik logunu kaydet",
                        FileName = $"guvenlik-log-{DateTime.Now:yyyyMMdd-HHmm}.jsonl",
                        Filter = "Log (JSONL)|*.jsonl|Tum dosyalar|*.*"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        await SendResponseAsync(
                            message,
                            "logs.download",
                            false,
                            new { message = "Islem iptal edildi.", cancelled = true },
                            null);
                        return;
                    }

                    File.Copy(AppPaths.SecurityLogPath, dialog.FileName, true);
                    await SendResponseAsync(message, "logs.download", true, new { message = "Log kaydedildi." }, null);
                    return;
                }
                case "archive.export":
                {
                    if (!_sessionState.IsAuthenticated || _sessionState.CurrentUser == null)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var request = DeserializePayload<ArchiveExportRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.Format))
                    {
                        await SendErrorAsync(message, "Arsiv formati okunamadi.");
                        return;
                    }

                    var normalized = request.Format.Trim().ToLowerInvariant();
                    if (normalized is not ("json" or "csv"))
                    {
                        await SendErrorAsync(message, "Arsiv formati desteklenmiyor.");
                        return;
                    }

                    var rangeDays = request.RangeDays.HasValue && request.RangeDays.Value > 0
                        ? request.RangeDays.Value
                        : (int?)null;
                    var since = rangeDays.HasValue
                        ? DateTimeOffset.UtcNow.AddDays(-rangeDays.Value)
                        : (DateTimeOffset?)null;

                    var records = await _chatArchiveStore.LoadRangeAsync(
                        _sessionState.CurrentUser.Username,
                        since,
                        CancellationToken.None);

                    var entries = await BuildArchiveExportAsync(records, CancellationToken.None);
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Sohbet arsivini kaydet",
                        FileName = $"sohbet-arsivi-{DateTime.Now:yyyyMMdd-HHmm}.{normalized}",
                        Filter = normalized == "json"
                            ? "JSON dosyasi|*.json|Tum dosyalar|*.*"
                            : "CSV dosyasi|*.csv|Tum dosyalar|*.*"
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        await SendResponseAsync(
                            message,
                            "archive.export",
                            false,
                            new { message = "Islem iptal edildi.", cancelled = true },
                            null);
                        return;
                    }

                    var payloadText = normalized == "json"
                        ? JsonSerializer.Serialize(entries, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })
                        : BuildCsv(entries);
                    await File.WriteAllTextAsync(dialog.FileName, payloadText);
                    await SendResponseAsync(
                        message,
                        "archive.export",
                        true,
                        new { message = $"Arsiv kaydedildi. ({entries.Count} kayit)" },
                        null);
                    return;
                }
                case "archive.clear":
                {
                    if (!_sessionState.IsAuthenticated || _sessionState.CurrentUser == null)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var request = DeserializePayload<ArchiveClearRequest>(message.Payload);
                    var rangeDays = request?.RangeDays.HasValue == true && request.RangeDays.Value > 0
                        ? request.RangeDays.Value
                        : (int?)null;
                    var since = rangeDays.HasValue
                        ? DateTimeOffset.UtcNow.AddDays(-rangeDays.Value)
                        : (DateTimeOffset?)null;

                    await _chatArchiveStore.ClearAsync(
                        _sessionState.CurrentUser.Username,
                        since,
                        CancellationToken.None);

                    await _chatService.LoadHistoryAsync(_sessionState.CurrentUser, CancellationToken.None);
                    ScheduleSnapshotPush();
                    await SendResponseAsync(
                        message,
                        "archive.clear",
                        true,
                        new { message = "Arsiv temizlendi." },
                        null);
                    return;
                }
                case "chat.pickFile":
                {
                    if (!_sessionState.IsAuthenticated)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var request = DeserializePayload<ChatPickFileRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.ThreadId))
                    {
                        await SendErrorAsync(message, "Dosya istegi okunamadi.");
                        return;
                    }

                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Dosya Sec",
                        CheckFileExists = true,
                        Multiselect = false
                    };

                    if (dialog.ShowDialog() != true)
                    {
                        await SendResponseAsync(
                            message,
                            "chat.pickFile",
                            false,
                            new { message = "Dosya secilmedi.", cancelled = true },
                            null);
                        return;
                    }

                    var filePath = dialog.FileName;
                    if (!File.Exists(filePath))
                    {
                        await SendErrorAsync(message, "Dosya bulunamadi.");
                        return;
                    }

                    var info = new FileInfo(filePath);
                    var contentType = GetContentType(info.Extension);
                    var scan = await _fileScanService.ScanAsync(
                        new FileScanRequest(info.Name, info.Length, contentType),
                        default);
                    var preview = await TryBuildImagePreviewFromFileAsync(filePath, contentType, info.Length);

                    ChatMessageDto messageDto;
                    try
                    {
                        messageDto = await _chatService.AddFileMessageAsync(
                            request.ThreadId,
                            info.Name,
                            info.Length,
                            scan.Status,
                            CancellationToken.None,
                            null,
                            contentType,
                            preview,
                            null);
                    }
                    catch (InvalidOperationException ex)
                    {
                        await SendErrorAsync(message, ex.Message);
                        return;
                    }

                    await SendResponseAsync(message, "chat.pickFile", true, new { message = messageDto, scan }, null);
                    _ = SendNetworkFileFromPathAsync(request.ThreadId, filePath, contentType, messageDto.Id);
                    return;
                }
                case "chat.attach":
                {
                    if (!_sessionState.IsAuthenticated)
                    {
                        await SendErrorAsync(message, "Oturum bulunamadi.");
                        return;
                    }

                    var request = DeserializePayload<ChatAttachRequest>(message.Payload);
                    if (request == null)
                    {
                        await SendErrorAsync(message, "Dosya verisi okunamadi.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(request.DataBase64))
                    {
                        await SendErrorAsync(message, "Dosya icerigi bulunamadi.");
                        return;
                    }

                    byte[] fileBytes;
                    try
                    {
                        fileBytes = Convert.FromBase64String(request.DataBase64);
                    }
                    catch (FormatException)
                    {
                        await SendErrorAsync(message, "Dosya verisi bozuk.");
                        return;
                    }

                    var actualSize = fileBytes.LongLength;
                    var scan = await _fileScanService.ScanAsync(
                        new FileScanRequest(request.FileName, actualSize, request.ContentType),
                        default);
                    var preview = TryBuildImagePreviewFromBase64(request.FileName, request.ContentType, actualSize, request.DataBase64);

                    ChatMessageDto messageDto;
                    try
                    {
                        messageDto = await _chatService.AddFileMessageAsync(
                            request.ThreadId,
                            request.FileName,
                            actualSize,
                            scan.Status,
                            CancellationToken.None,
                            null,
                            request.ContentType,
                            preview,
                            null);
                    }
                    catch (InvalidOperationException ex)
                    {
                        await SendErrorAsync(message, ex.Message);
                        return;
                    }

                    await SendResponseAsync(message, "chat.attach", true, new { message = messageDto, scan }, null);
                    _ = SendNetworkFileAsync(request.ThreadId, request.FileName, fileBytes, request.ContentType, messageDto.Id);
                    return;
                }
                case "file.open":
                {
                    var request = DeserializePayload<FileActionRequest>(message.Payload);
                    if (request == null || string.IsNullOrWhiteSpace(request.Path))
                    {
                        await SendErrorAsync(message, "Dosya yolu okunamadi.");
                        return;
                    }

                    var fullPath = Path.GetFullPath(request.Path);
                    var receivedRoot = Path.GetFullPath(AppPaths.ReceivedFilesPath);
                    if (!fullPath.StartsWith(receivedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        await SendErrorAsync(message, "Dosya yolu izinli degil.");
                        return;
                    }

                    if (!File.Exists(fullPath))
                    {
                        await SendErrorAsync(message, "Dosya bulunamadi.");
                        return;
                    }

                    try
                    {
                        if (string.Equals(request.Action, "reveal", StringComparison.OrdinalIgnoreCase))
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"")
                            {
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendErrorAsync(message, $"Dosya acilamadi: {ex.Message}");
                        return;
                    }

                    await SendResponseAsync(message, "file.open", true, new { message = "Dosya acildi." }, null);
                    return;
                }
                default:
                    await SendErrorAsync(message, "Islem taninmiyor.");
                    return;
            }
        }

        private T? DeserializePayload<T>(JsonElement payload)
        {
            if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return default;
            }

            return payload.Deserialize<T>(_jsonOptions);
        }

        private Task SendErrorAsync(WebMessage message, string error)
        {
            var errors = new List<ValidationError> { new("general", error) };
            return SendResponseAsync(message, message.Type, false, new { message = error }, errors);
        }

        private Task SendResponseAsync(WebMessage message, string responseType, bool ok, object? payload, IReadOnlyList<ValidationError>? errors)
        {
            var response = new WebResponse(message.Id, responseType, ok, payload, errors);
            var json = JsonSerializer.Serialize(response, _jsonOptions);
            MessengerView.CoreWebView2.PostWebMessageAsJson(json);
            return Task.CompletedTask;
        }
    }
}
