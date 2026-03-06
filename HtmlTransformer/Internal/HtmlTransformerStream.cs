using System.Text;
using HtmlTransformer.Feature.Replacement;
using Microsoft.AspNetCore.Http;

namespace HtmlTransformer.Internal;

public sealed class HtmlTransformerStream : Stream
{
    private static readonly byte[] HeadStartTagBytes = "<head"u8.ToArray();
    private static readonly byte[] HeadEndTagBytes = "</head>"u8.ToArray();
    private static readonly byte[] BodyStartTagBytes = "<body"u8.ToArray();
    private static readonly byte[] BodyEndTagBytes = "</body>"u8.ToArray();

    private readonly HttpResponse _response;
    private readonly Stream _inner;
    private readonly Func<Stream, CancellationToken, Task>? _injectAtHeadStart;
    private readonly Func<Stream, CancellationToken, Task>? _injectAtHeadEnd;
    private readonly Func<Stream, CancellationToken, Task>? _injectAtBodyEnd;
    private readonly Func<IList<ReplacementEntry>?> _replacements;

    private readonly MemoryStream _buffer = new();
    private readonly int _scanLimitBytes = 64 * 1024;

    private bool _passthrough;
    private bool _completed;
    private bool _headersAdjusted;
    private bool _bodyEndInjected;
    private readonly HashSet<string> _appliedFirstReplacements = new();

    public HtmlTransformerStream(
        HttpResponse response,
        Stream inner,
        Func<Stream, CancellationToken, Task>? injectAtHeadStart = null,
        Func<Stream, CancellationToken, Task>? injectAtHeadEnd = null,
        Func<Stream, CancellationToken, Task>? injectAtBodyEnd = null,
        Func<IList<ReplacementEntry>?> replacements = null!)
    {
        _response = response;
        _inner = inner;
        _injectAtHeadStart = injectAtHeadStart;
        _injectAtHeadEnd = injectAtHeadEnd;
        _injectAtBodyEnd = injectAtBodyEnd;
        _replacements = replacements ?? (() => null);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void PrepareHeaders()
    {
        if (_headersAdjusted)
        {
            return;
        }

        if (!_response.HasStarted)
        {
            _response.ContentLength = null;
        }

        _headersAdjusted = true;
    }

    public override void Flush()
    {
        FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _inner.FlushAsync(cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The response body stream has already been completed.");
        }

        if (_passthrough)
        {
            if (!_bodyEndInjected && _injectAtBodyEnd is not null)
            {
                var bodyEndIndex = IndexOfAsciiIgnoreCase(buffer.Span, BodyEndTagBytes);
                if (bodyEndIndex >= 0)
                {
                    // Write up to </body>
                    await WriteWithReplacementsAsync(buffer.Slice(0, bodyEndIndex), cancellationToken);

                    // Inject at BodyEnd
                    await _injectAtBodyEnd(_inner, cancellationToken);
                    _bodyEndInjected = true;

                    // Write from </body> to end
                    await WriteWithReplacementsAsync(buffer.Slice(bodyEndIndex, buffer.Length - bodyEndIndex), cancellationToken);
                    return;
                }
            }

            await WriteWithReplacementsAsync(buffer, cancellationToken);
            return;
        }

        PrepareHeaders();

        await _buffer.WriteAsync(buffer, cancellationToken);

        if (await TryInjectAndWriteAsync(cancellationToken))
        {
            _passthrough = true;
            _buffer.SetLength(0);
            return;
        }

        if (_buffer.Length > _scanLimitBytes)
        {
            _passthrough = true;
            _buffer.Position = 0;
            // Note: when falling back to passthrough, we don't apply replacements to the already buffered content
            // unless we call TryInjectAndWriteAsync one last time. 
            // But if we are here, it means TryInjectAndWriteAsync returned false (didn't find tags).
            // We should still probably apply replacements if there are any.
            
            var data = _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);
            await WriteWithReplacementsAsync(data, cancellationToken);
            _buffer.SetLength(0);
        }
    }

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        PrepareHeaders();

        if (!_passthrough && _buffer.Length > 0)
        {
            if (!await TryInjectAndWriteAsync(cancellationToken))
            {
                var data = _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);
                await WriteWithReplacementsAsync(data, cancellationToken);
            }

            _buffer.SetLength(0);
        }

        if (!_bodyEndInjected && _injectAtBodyEnd is not null)
        {
            // Fallback: inject at the end of the stream
            await _injectAtBodyEnd(_inner, cancellationToken);
            _bodyEndInjected = true;
        }

        await _inner.FlushAsync(cancellationToken);
    }

    private async Task<bool> TryInjectAndWriteAsync(CancellationToken ct)
    {
        var data = _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);

        var headStartIndex = IndexOfAsciiIgnoreCase(data.Span, HeadStartTagBytes);
        if (headStartIndex >= 0)
        {
            var headOpenEndIndex = IndexOfByte(data.Span, (byte)'>', headStartIndex + HeadStartTagBytes.Length);
            if (headOpenEndIndex < 0)
            {
                return false;
            }

            var headEndIndex = IndexOfAsciiIgnoreCase(data.Span, HeadEndTagBytes);
            if (headEndIndex < 0)
            {
                return false;
            }

            var bodyEndIndex = IndexOfAsciiIgnoreCase(data.Span, BodyEndTagBytes);

            // 1. Write up to <head...>
            await WriteWithReplacementsAsync(data.Slice(0, headOpenEndIndex + 1), ct);

            // 2. Inject at HeadStart
            if (_injectAtHeadStart is not null) await _injectAtHeadStart(_inner, ct);

            // 3. Write from <head...> to </head>
            await WriteWithReplacementsAsync(data.Slice(headOpenEndIndex + 1, headEndIndex - (headOpenEndIndex + 1)), ct);

            // 4. Inject at HeadEnd
            if (_injectAtHeadEnd is not null) await _injectAtHeadEnd(_inner, ct);

            if (bodyEndIndex >= 0)
            {
                // 5. Write from </head> to </body>
                await WriteWithReplacementsAsync(data.Slice(headEndIndex, bodyEndIndex - headEndIndex), ct);

                // 6. Inject at BodyEnd
                if (_injectAtBodyEnd is not null)
                {
                    await _injectAtBodyEnd(_inner, ct);
                    _bodyEndInjected = true;
                }

                // 7. Write from </body> to end
                await WriteWithReplacementsAsync(data.Slice(bodyEndIndex, (int)_buffer.Length - bodyEndIndex), ct);
            }
            else
            {
                // 5. Write from </head> to end
                await WriteWithReplacementsAsync(data.Slice(headEndIndex, (int)_buffer.Length - headEndIndex), ct);
            }

            return true;
        }

        var bodyStartIndex = IndexOfAsciiIgnoreCase(data.Span, BodyStartTagBytes);
        if (bodyStartIndex >= 0)
        {
            var bodyEndIndex = IndexOfAsciiIgnoreCase(data.Span, BodyEndTagBytes);

            // Write up to <body
            await WriteWithReplacementsAsync(data.Slice(0, bodyStartIndex), ct);

            // Inject synthetic head
            await _inner.WriteAsync("<head>"u8.ToArray(), ct);
            if (_injectAtHeadStart is not null) await _injectAtHeadStart(_inner, ct);
            if (_injectAtHeadEnd is not null) await _injectAtHeadEnd(_inner, ct);
            await _inner.WriteAsync("</head>"u8.ToArray(), ct);

            if (bodyEndIndex >= 0)
            {
                // Write from <body to </body>
                await WriteWithReplacementsAsync(data.Slice(bodyStartIndex, bodyEndIndex - bodyStartIndex), ct);

                // Inject at BodyEnd
                if (_injectAtBodyEnd is not null)
                {
                    await _injectAtBodyEnd(_inner, ct);
                    _bodyEndInjected = true;
                }

                // Write from </body> to end
                await WriteWithReplacementsAsync(data.Slice(bodyEndIndex, (int)_buffer.Length - bodyEndIndex), ct);
            }
            else
            {
                // Write from <body to end
                await WriteWithReplacementsAsync(data.Slice(bodyStartIndex, (int)_buffer.Length - bodyStartIndex), ct);
            }

            return true;
        }

        return false;
    }

    private async Task WriteWithReplacementsAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        var replacements = _replacements();
        if (replacements == null || replacements.Count == 0)
        {
            await _inner.WriteAsync(data, ct);
            return;
        }

        var current = data;
        while (current.Length > 0)
        {
            var bestMatchIndex = -1;
            ReplacementEntry bestMatchEntry = default;

            foreach (var replacement in replacements)
            {
                if (replacement.Scope == ReplacementScope.First && _appliedFirstReplacements.Contains(replacement.Placeholder))
                {
                    continue;
                }

                var placeholderBytes = Encoding.UTF8.GetBytes(replacement.Placeholder);
                var index = IndexOfBytes(current.Span, placeholderBytes);
                if (index >= 0 && (bestMatchIndex == -1 || index < bestMatchIndex))
                {
                    bestMatchIndex = index;
                    bestMatchEntry = replacement;
                }
            }

            if (bestMatchIndex >= 0)
            {
                // Write prefix
                if (bestMatchIndex > 0)
                {
                    await _inner.WriteAsync(current.Slice(0, bestMatchIndex), ct);
                }

                // Write replacement
                await bestMatchEntry.WriteAsync.Invoke(_inner, ct);
                if (bestMatchEntry.Scope == ReplacementScope.First)
                {
                    _appliedFirstReplacements.Add(bestMatchEntry.Placeholder);
                }

                // Advance
                var placeholderBytes = Encoding.UTF8.GetBytes(bestMatchEntry.Placeholder);
                current = current.Slice(bestMatchIndex + placeholderBytes.Length);
            }
            else
            {
                // No more replacements in this chunk
                await _inner.WriteAsync(current, ct);
                break;
            }
        }
    }

    private static int IndexOfBytes(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0) return 0;
        return haystack.IndexOf(needle);
    }

    private static int IndexOfByte(ReadOnlySpan<byte> data, byte value, int startIndex)
    {
        for (var i = startIndex; i < data.Length; i++)
        {
            if (data[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfAsciiIgnoreCase(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var matched = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (ToLowerAscii(haystack[i + j]) != ToLowerAscii(needle[j]))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return i;
            }
        }

        return -1;
    }

    private static byte ToLowerAscii(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z'
            ? (byte)(value + 32)
            : value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
}