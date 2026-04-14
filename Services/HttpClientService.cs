using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PKS3.Models;

namespace PKS3.Services;

public sealed class HttpClientService : IDisposable
{
    private readonly HttpClient _httpClient = new();

    public event Action<HttpLogEntry>? LogEntryCreated;

    public async Task<string> SendAsync(string url, string method, string? jsonBody, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            request.Content = new StringContent(jsonBody ?? "", Encoding.UTF8, "application/json");
        }

        string responseBody = "";
        HttpStatusCode? statusCode = null;

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            statusCode = response.StatusCode;
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return responseBody;
        }
        finally
        {
            var elapsedMs = ElapsedMs(startedAt);
            var entry = new HttpLogEntry
            {
                Timestamp = DateTimeOffset.Now,
                Direction = HttpDirection.Outgoing,
                Method = method,
                Url = url,
                StatusCode = statusCode,
                DurationMs = elapsedMs,
                RequestBody = jsonBody ?? "",
                ResponseBody = responseBody
            };
            LogEntryCreated?.Invoke(entry);
        }
    }

    private static long ElapsedMs(long startedTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startedTimestamp;
        return (long)(elapsedTicks * 1000.0 / Stopwatch.Frequency);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
