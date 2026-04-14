using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Navigation;
using System.Windows.Threading;
using PKS3.Models;
using PKS3.Services;

namespace PKS3;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly HttpServerService _server = new();
    private readonly HttpClientService _client = new();
    private readonly LogFileWriter _logFileWriter;
    private readonly DispatcherTimer _chartTimer;

    private readonly ObservableCollection<HttpLogEntry> _logs = new();
    private ICollectionView _logsView = null!;

    private string _serverPort = "8080";
    private string _serverButtonText = "Запустить сервер";
    private string _serverStatusLine = "Сервер: остановлен";
    private bool _isServerRunning;
    private Uri? _serverUrl;
    private string _serverUrlText = "";

    private long _getCount;
    private long _postCount;
    private long _totalCount;
    private long _avgMs;

    private string _logText = "";

    private string _selectedMethodFilter = "Все";
    private string _selectedStatusFilter = "Все";

    private string _clientUrl = "https://jsonplaceholder.typicode.com/posts";
    private string _clientMethod = "GET";
    private string _clientJsonBody = "{\n  \"message\": \"hello\"\n}";
    private string _clientResponseText = "";

    private string _selectedLoadChartMode = "Минуты";

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _logFileWriter = new LogFileWriter(Path.Combine(appDir, "logs.txt"));

        _logsView = CollectionViewSource.GetDefaultView(_logs);
        _logsView.Filter = LogsFilter;

        _server.LogEntryCreated += OnLogEntryCreated;
        _server.StatsChanged += UpdateStats;
        _client.LogEntryCreated += OnLogEntryCreated;

        _chartTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _chartTimer.Tick += (_, _) => RebuildLoadChart();
        _chartTimer.Start();

        UpdateStats();
        RebuildLoadChart();
    }

    public ObservableCollection<string> MethodFilters { get; } = new(new[] { "Все", "GET", "POST" });
    public ObservableCollection<string> StatusFilters { get; } = new(new[] { "Все", "2xx", "4xx", "5xx" });
    public ObservableCollection<string> ClientMethods { get; } = new(new[] { "GET", "POST" });
    public ObservableCollection<string> LoadChartModes { get; } = new(new[] { "Минуты", "Часы" });

    public ObservableCollection<LoadPoint> LoadPoints { get; } = new();
    public ObservableCollection<StatItem> ServerStatsTable { get; } = new();

    public ICollectionView LogsView => _logsView;

    public string ServerPort
    {
        get => _serverPort;
        set
        {
            if (value == _serverPort) return;
            _serverPort = value;
            OnPropertyChanged();
        }
    }

    public string ServerButtonText
    {
        get => _serverButtonText;
        set
        {
            if (value == _serverButtonText) return;
            _serverButtonText = value;
            OnPropertyChanged();
        }
    }

    public string ServerStatusLine
    {
        get => _serverStatusLine;
        set
        {
            if (value == _serverStatusLine) return;
            _serverStatusLine = value;
            OnPropertyChanged();
        }
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        set
        {
            if (value == _isServerRunning) return;
            _isServerRunning = value;
            OnPropertyChanged();
        }
    }

    public Uri? ServerUrl
    {
        get => _serverUrl;
        set
        {
            if (Equals(value, _serverUrl)) return;
            _serverUrl = value;
            OnPropertyChanged();
        }
    }

    public string ServerUrlText
    {
        get => _serverUrlText;
        set
        {
            if (value == _serverUrlText) return;
            _serverUrlText = value;
            OnPropertyChanged();
        }
    }

    public long GetCount
    {
        get => _getCount;
        set
        {
            if (value == _getCount) return;
            _getCount = value;
            OnPropertyChanged();
        }
    }

    public long PostCount
    {
        get => _postCount;
        set
        {
            if (value == _postCount) return;
            _postCount = value;
            OnPropertyChanged();
        }
    }

    public long TotalCount
    {
        get => _totalCount;
        set
        {
            if (value == _totalCount) return;
            _totalCount = value;
            OnPropertyChanged();
        }
    }

    public long AvgMs
    {
        get => _avgMs;
        set
        {
            if (value == _avgMs) return;
            _avgMs = value;
            OnPropertyChanged();
        }
    }

    public string LogText
    {
        get => _logText;
        set
        {
            if (value == _logText) return;
            _logText = value;
            OnPropertyChanged();
        }
    }

    public string SelectedMethodFilter
    {
        get => _selectedMethodFilter;
        set
        {
            if (value == _selectedMethodFilter) return;
            _selectedMethodFilter = value;
            OnPropertyChanged();
            _logsView.Refresh();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (value == _selectedStatusFilter) return;
            _selectedStatusFilter = value;
            OnPropertyChanged();
            _logsView.Refresh();
        }
    }

    public string ClientUrl
    {
        get => _clientUrl;
        set
        {
            if (value == _clientUrl) return;
            _clientUrl = value;
            OnPropertyChanged();
        }
    }

    public string ClientMethod
    {
        get => _clientMethod;
        set
        {
            if (value == _clientMethod) return;
            _clientMethod = value;
            OnPropertyChanged();
        }
    }

    public string ClientJsonBody
    {
        get => _clientJsonBody;
        set
        {
            if (value == _clientJsonBody) return;
            _clientJsonBody = value;
            OnPropertyChanged();
        }
    }

    public string ClientResponseText
    {
        get => _clientResponseText;
        set
        {
            if (value == _clientResponseText) return;
            _clientResponseText = value;
            OnPropertyChanged();
        }
    }

    public string SelectedLoadChartMode
    {
        get => _selectedLoadChartMode;
        set
        {
            if (value == _selectedLoadChartMode) return;
            _selectedLoadChartMode = value;
            OnPropertyChanged();
            RebuildLoadChart();
        }
    }

    private async void StartStopServer_Click(object sender, RoutedEventArgs e)
    {
        if (_server.IsRunning)
        {
            await _server.StopAsync();
            ServerButtonText = "Запустить сервер";
            ServerStatusLine = "Сервер: остановлен";
            IsServerRunning = false;
            ServerUrl = null;
            ServerUrlText = "";
            AppendTextLog("SERVER STOPPED");
            return;
        }

        if (!int.TryParse(ServerPort, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show("Введите корректный порт (1..65535).", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _server.Start(port);
            ServerButtonText = "Остановить сервер";
            ServerStatusLine = $"Сервер: запущен на http://localhost:{port}/";
            IsServerRunning = true;
            ServerUrlText = $"http://localhost:{port}/";
            ServerUrl = new Uri(ServerUrlText);
            AppendTextLog($"SERVER STARTED on http://localhost:{port}/");

            ClientUrl = $"http://localhost:{port}/";
        }
        catch (HttpListenerException ex)
        {
            AppendTextLog($"ERROR starting server: {ex.Message}");
            MessageBox.Show(
                "Не удалось запустить HttpListener.\n\n" +
                "Возможные причины:\n" +
                "- нет прав на регистрацию URL (попробуйте запуск от администратора)\n" +
                "- URLACL не настроен\n\n" +
                $"Детали: {ex.Message}",
                "Ошибка запуска сервера",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppendTextLog($"ERROR starting server: {ex}");
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SendClientRequest_Click(object sender, RoutedEventArgs e)
    {
        var url = (ClientUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Введите URL.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var method = ClientMethod;
        var json = string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ? (ClientJsonBody ?? "") : null;

        try
        {
            ClientResponseText = "Отправка запроса...";
            var resp = await _client.SendAsync(url, method, json, CancellationToken.None);
            ClientResponseText = resp;
        }
        catch (Exception ex)
        {
            ClientResponseText = ex.ToString();
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _logs.Clear();
        LogText = "";
        RebuildLoadChart();
        UpdateStats();
    }

    private bool LogsFilter(object obj)
    {
        if (obj is not HttpLogEntry e) return false;

        if (SelectedMethodFilter != "Все" && !string.Equals(e.Method, SelectedMethodFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedStatusFilter != "Все")
        {
            var code = (int?)(e.StatusCode);
            if (code is null) return false;

            var group = code.Value / 100;
            if (SelectedStatusFilter == "2xx" && group != 2) return false;
            if (SelectedStatusFilter == "4xx" && group != 4) return false;
            if (SelectedStatusFilter == "5xx" && group != 5) return false;
        }

        return true;
    }

    private void OnLogEntryCreated(HttpLogEntry entry)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _logs.Add(entry);
            _logsView.Refresh();

            var line = FormatEntry(entry);
            AppendTextLog(line);
            _ = _logFileWriter.AppendAsync(line, CancellationToken.None);

            RebuildLoadChart();
            UpdateStats();
        });
    }

    private void UpdateStats()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var incoming = _logs.Where(l => l.Direction == HttpDirection.Incoming).ToArray();
            GetCount = incoming.LongCount(l => string.Equals(l.Method, "GET", StringComparison.OrdinalIgnoreCase));
            PostCount = incoming.LongCount(l => string.Equals(l.Method, "POST", StringComparison.OrdinalIgnoreCase));
            TotalCount = incoming.LongLength;

            AvgMs = incoming.Length == 0 ? 0 : (long)incoming.Average(l => (double)l.DurationMs);

            if (_server.IsRunning && _server.Port is not null)
            {
                ServerStatusLine = $"Сервер: запущен на http://localhost:{_server.Port}/";
                ServerButtonText = "Остановить сервер";
                IsServerRunning = true;
                ServerUrlText = $"http://localhost:{_server.Port}/";
                ServerUrl = new Uri(ServerUrlText);
            }
            else
            {
                IsServerRunning = false;
                ServerUrl = null;
                ServerUrlText = "";
            }

            var uptimeSeconds = 0L;
            if (_server.IsRunning && _server.StartedAt is not null)
            {
                uptimeSeconds = (long)(DateTimeOffset.Now - _server.StartedAt.Value).TotalSeconds;
            }

            ServerStatsTable.Clear();
            ServerStatsTable.Add(new StatItem { Name = "GET запросов", Value = GetCount.ToString() });
            ServerStatsTable.Add(new StatItem { Name = "POST запросов", Value = PostCount.ToString() });
            ServerStatsTable.Add(new StatItem { Name = "Всего запросов", Value = TotalCount.ToString() });
            ServerStatsTable.Add(new StatItem { Name = "Среднее время (мс)", Value = AvgMs.ToString() });
            ServerStatsTable.Add(new StatItem { Name = "Uptime (сек)", Value = uptimeSeconds.ToString() });
        });
    }

    private void ServerLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    private void RebuildLoadChart()
    {
        var now = DateTimeOffset.Now;
        var incoming = _logs.Where(l => l.Direction == HttpDirection.Incoming).ToArray();

        LoadPoints.Clear();

        if (SelectedLoadChartMode == "Часы")
        {
            var from = now.AddHours(-23);
            var buckets = Enumerable.Range(0, 24).Select(i => new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, 0, 0, from.Offset).AddHours(i)).ToArray();

            foreach (var hour in buckets)
            {
                var next = hour.AddHours(1);
                var count = incoming.Count(l => l.Timestamp >= hour && l.Timestamp < next);
                var height = count == 0 ? 2 : Math.Min(120, count * 6);
                LoadPoints.Add(new LoadPoint { Label = hour.ToString("HH"), Count = height });
            }
        }
        else
        {
            var from = now.AddMinutes(-19);
            var buckets = Enumerable.Range(0, 20).Select(i => new DateTimeOffset(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0, from.Offset).AddMinutes(i)).ToArray();

            foreach (var minute in buckets)
            {
                var next = minute.AddMinutes(1);
                var count = incoming.Count(l => l.Timestamp >= minute && l.Timestamp < next);
                var height = count == 0 ? 2 : Math.Min(120, count * 4);
                LoadPoints.Add(new LoadPoint { Label = minute.ToString("HH:mm"), Count = height });
            }
        }
    }

    private void AppendTextLog(string line)
    {
        var sb = new StringBuilder(LogText);
        if (sb.Length > 0) sb.AppendLine();
        sb.Append(line);

        const int maxChars = 80_000;
        if (sb.Length > maxChars)
        {
            sb.Remove(0, sb.Length - maxChars);
        }

        LogText = sb.ToString();
    }

    private static string FormatEntry(HttpLogEntry e)
    {
        var code = e.StatusCode is null ? "-" : ((int)e.StatusCode).ToString();
        var headers = Trim(e.RequestHeaders);
        var req = Trim(e.RequestBody);
        var resp = Trim(e.ResponseBody);
        return $"{e.Timestamp:O} | {e.Direction} | {e.Method} {e.Url} | {code} | {e.DurationMs}ms | headers: {headers} | req: {req} | resp: {resp}";
    }

    private static string Trim(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length <= 160 ? s : s[..160] + "...";
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _chartTimer.Stop();

        try { await _server.StopAsync(); } catch { /* ignore */ }
        _server.Dispose();
        _client.Dispose();
        _logFileWriter.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
