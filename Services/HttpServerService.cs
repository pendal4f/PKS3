using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PKS3.Models;

namespace PKS3.Services;

public sealed class HttpServerService : IDisposable
{
    private readonly ConcurrentDictionary<Guid, string> _messages = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    private long _totalRequests;
    private long _getRequests;
    private long _postRequests;
    private long _totalProcessingMs;

    private DateTimeOffset? _startedAt;

    public bool IsRunning => _listener is not null && _listener.IsListening;
    public int? Port { get; private set; }
    public DateTimeOffset? StartedAt => _startedAt;

    public event Action<HttpLogEntry>? LogEntryCreated;
    public event Action? StatsChanged;

    public void Start(int port)
    {
        if (IsRunning) return;

        Port = port;
        _startedAt = DateTimeOffset.Now;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        var cts = _cts;
        var listener = _listener;
        var acceptLoopTask = _acceptLoopTask;

        _cts = null;
        _listener = null;
        _acceptLoopTask = null;

        try { cts?.Cancel(); } catch { /* ignore */ }
        try { listener?.Stop(); listener?.Close(); } catch { /* ignore */ }

        if (acceptLoopTask is not null)
        {
            try { await acceptLoopTask.ConfigureAwait(false); } catch { /* ignore */ }
        }
    }

    public object GetStatus()
    {
        var startedAt = _startedAt;
        var uptime = startedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - startedAt.Value;

        var total = Interlocked.Read(ref _totalRequests);
        var avgMs = total == 0 ? 0 : Interlocked.Read(ref _totalProcessingMs) / total;

        return new
        {
            port = Port,
            startedAt,
            uptimeSeconds = (long)uptime.TotalSeconds,
            totalRequests = total,
            getRequests = Interlocked.Read(ref _getRequests),
            postRequests = Interlocked.Read(ref _postRequests),
            averageProcessingMs = avgMs
        };
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var listener = _listener;
        if (listener is null) return;

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch
            {
                if (cancellationToken.IsCancellationRequested) break;
            }

            if (context is null) continue;

            // многопоточность: каждый запрос обрабатывается независимо
            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var request = context.Request;
        var response = context.Response;

        string requestBody = "";
        string responseBody = "";
        HttpStatusCode statusCode = HttpStatusCode.OK;

        try
        {
            requestBody = await ReadRequestBodyAsync(request).ConfigureAwait(false);

            if (string.Equals(request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _getRequests);
                responseBody = JsonSerializer.Serialize(GetStatus(), new JsonSerializerOptions { WriteIndented = true });
                statusCode = HttpStatusCode.OK;
            }
            else if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _postRequests);

                var message = TryParseMessage(requestBody);
                if (message is null)
                {
                    statusCode = HttpStatusCode.BadRequest;
                    responseBody = JsonSerializer.Serialize(new { error = "Invalid JSON. Expected { \"message\": \"...\" }" });
                }
                else
                {
                    var id = Guid.NewGuid();
                    _messages[id] = message;
                    statusCode = HttpStatusCode.OK;
                    responseBody = JsonSerializer.Serialize(new { id });
                }
            }
            else
            {
                statusCode = HttpStatusCode.MethodNotAllowed;
                responseBody = JsonSerializer.Serialize(new { error = "Only GET and POST are supported." });
            }
        }
        catch (Exception ex)
        {
            statusCode = HttpStatusCode.InternalServerError;
            responseBody = JsonSerializer.Serialize(new { error = ex.Message });
        }
        finally
        {
            var elapsedMs = ElapsedMs(startedAt);

            Interlocked.Increment(ref _totalRequests);
            Interlocked.Add(ref _totalProcessingMs, elapsedMs);
            StatsChanged?.Invoke();

            var entry = new HttpLogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Direction = HttpDirection.Incoming,
                Method = request.HttpMethod ?? "",
                Url = request.Url?.ToString() ?? "",
                StatusCode = statusCode,
                DurationMs = elapsedMs,
                RequestHeaders = FormatHeaders(request.Headers),
                RequestBody = requestBody,
                ResponseBody = responseBody
            };

            LogEntryCreated?.Invoke(entry);

            try
            {
                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json; charset=utf-8";
                var buffer = Encoding.UTF8.GetBytes(responseBody);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
            finally
            {
                try { response.OutputStream.Close(); } catch { /* ignore */ }
            }
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return "";
        using var stream = request.InputStream;
        using var reader = new StreamReader(stream, request.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static string? TryParseMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("message", out var messageProp)) return null;
            return messageProp.ValueKind == JsonValueKind.String ? messageProp.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatHeaders(System.Collections.Specialized.NameValueCollection headers)
    {
        if (headers.Count == 0) return "";

        var sb = new StringBuilder();
        foreach (var keyObj in headers.AllKeys)
        {
            if (keyObj is null) continue;
            sb.Append(keyObj).Append(": ").Append(headers[keyObj]).AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static long ElapsedMs(long startedTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startedTimestamp;
        return (long)(elapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        _cts?.Dispose();
    }
}
