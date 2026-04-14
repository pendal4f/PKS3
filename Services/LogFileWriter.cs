using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PKS3.Services;

public sealed class LogFileWriter : IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LogFileWriter(string filePath)
    {
        _filePath = filePath;
    }

    public async Task AppendAsync(string line, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.AppendAllTextAsync(
                    _filePath,
                    line + Environment.NewLine,
                    Encoding.UTF8,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
