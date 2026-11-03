namespace Portal.Core.Files;

/// <summary>Stream copying that reports the running byte count.</summary>
public static class StreamCopy
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Copies everything from <paramref name="source"/> and reports total bytes copied so far.</summary>
    public static async Task CopyAsync(Stream source, Stream destination, IProgress<long> progress, CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
            progress.Report(total);
        }
        await destination.FlushAsync(ct).ConfigureAwait(false);
    }
}
