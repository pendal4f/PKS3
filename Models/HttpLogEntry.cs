using System;
using System.Net;

namespace PKS3.Models;

public sealed class HttpLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public HttpDirection Direction { get; init; }
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public HttpStatusCode? StatusCode { get; init; }
    public long DurationMs { get; init; }

    public string RequestHeaders { get; init; } = "";
    public string RequestBody { get; init; } = "";

    public string ResponseHeaders { get; init; } = "";
    public string ResponseBody { get; init; } = "";
}
