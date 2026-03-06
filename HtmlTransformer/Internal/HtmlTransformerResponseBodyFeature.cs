using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace HtmlTransformer.Internal;

public sealed class HtmlTransformerResponseBodyFeature : IHttpResponseBodyFeature
{
    private readonly IHttpResponseBodyFeature _inner;
    private readonly HtmlTransformerStream _stream;
    private PipeWriter? _writer;

    public HtmlTransformerResponseBodyFeature(IHttpResponseBodyFeature inner, HtmlTransformerStream stream)
    {
        _inner = inner;
        _stream = stream;
    }

    public Stream Stream => _stream;

    public PipeWriter Writer => _writer ??= PipeWriter.Create(_stream, new StreamPipeWriterOptions(leaveOpen: true));

    public void DisableBuffering()
    {
        _inner.DisableBuffering();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _stream.PrepareHeaders();
        return _inner.StartAsync(cancellationToken);
    }

    public async Task SendFileAsync(
        string path,
        long offset,
        long? count,
        CancellationToken cancellationToken = default)
    {
        _stream.PrepareHeaders();
        // Since we want to transform the content, we cannot use the optimized SendFileAsync of the inner feature.
        // We must read the file and write it to our stream.
        await using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (offset > 0)
        {
            fileStream.Seek(offset, SeekOrigin.Begin);
        }

        var bytesToRead = count ?? (fileStream.Length - offset);
        var buffer = new byte[8192];
        while (bytesToRead > 0)
        {
            var read = await fileStream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, bytesToRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await _stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            bytesToRead -= read;
        }
    }

    public async Task CompleteAsync()
    {
        if (_writer is not null)
        {
            await _writer.FlushAsync();
            await _writer.CompleteAsync();
        }

        await _stream.CompleteAsync();
        await _inner.CompleteAsync();
    }
}