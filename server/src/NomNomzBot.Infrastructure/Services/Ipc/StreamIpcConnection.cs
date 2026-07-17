// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;

namespace NomNomzBot.Infrastructure.Services.Ipc;

/// <summary>
/// Newline-delimited-JSON framing over any duplex <see cref="System.IO.Stream"/> — one class serves both
/// production endpoints, since a Unix-socket <c>NetworkStream</c> and a Windows
/// <c>NamedPipeServerStream</c> are both plain streams. Enforces the 64&#160;KB frame cap at the
/// transport, reporting a breach as <see cref="IpcReadResult.TooLarge"/> instead of buffering
/// unbounded input; writes are serialized so concurrent responses never interleave bytes.
/// </summary>
public sealed class StreamIpcConnection : IIpcConnection
{
    /// <summary>Hard per-frame cap — a dev-tool request has no business being larger.</summary>
    public const int MaxFrameBytes = 64 * 1024;

    private const int ReadChunkBytes = 4 * 1024;
    private const byte NewlineByte = (byte)'\n';

    private readonly System.IO.Stream _stream;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly List<byte> _pending = [];

    public StreamIpcConnection(System.IO.Stream stream)
    {
        _stream = stream;
    }

    public async Task<IpcReadResult> ReadFrameAsync(CancellationToken ct)
    {
        byte[] chunk = new byte[ReadChunkBytes];
        while (true)
        {
            int newlineIndex = _pending.IndexOf(NewlineByte);
            if (newlineIndex >= 0)
                return IpcReadResult.Of(TakeFrame(newlineIndex));

            if (_pending.Count > MaxFrameBytes)
                return IpcReadResult.TooLarge;

            int read;
            try
            {
                read = await _stream.ReadAsync(chunk, ct);
            }
            catch (IOException)
            {
                return IpcReadResult.Closed;
            }
            catch (ObjectDisposedException)
            {
                return IpcReadResult.Closed;
            }

            // EOF — any partial line without its delimiter is not a frame; the peer is gone.
            if (read == 0)
                return IpcReadResult.Closed;

            _pending.AddRange(chunk.AsSpan(0, read));
        }
    }

    public async Task WriteFrameAsync(string json, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
        await _writeGate.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(payload, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _writeGate.Dispose();
        await _stream.DisposeAsync();
    }

    /// <summary>Extracts the line before the delimiter and keeps any pipelined bytes after it.</summary>
    private string TakeFrame(int newlineIndex)
    {
        int length =
            newlineIndex > 0 && _pending[newlineIndex - 1] == (byte)'\r'
                ? newlineIndex - 1
                : newlineIndex;
        string frame = Encoding.UTF8.GetString(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending)[..length]
        );
        _pending.RemoveRange(0, newlineIndex + 1);
        return frame;
    }
}
