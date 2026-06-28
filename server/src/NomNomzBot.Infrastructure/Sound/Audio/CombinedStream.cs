// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.IO;

namespace NomNomzBot.Infrastructure.Sound.Audio;

/// <summary>
/// Presents an already-read byte header followed by the remainder of an upstream stream as a single
/// readable stream. Used to put back the bytes consumed during content-sniff validation without
/// buffering the entire upload in memory before sniffing.
/// </summary>
internal sealed class CombinedReadStream : System.IO.Stream
{
    private readonly byte[] _prefix;
    private readonly int _prefixLength;
    private readonly System.IO.Stream _rest;
    private int _prefixPos;

    public CombinedReadStream(byte[] prefix, int prefixLength, System.IO.Stream rest)
    {
        _prefix = prefix;
        _prefixLength = prefixLength;
        _rest = rest;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int prefixRemaining = _prefixLength - _prefixPos;
        if (prefixRemaining > 0)
        {
            int fromPrefix = Math.Min(count, prefixRemaining);
            Array.Copy(_prefix, _prefixPos, buffer, offset, fromPrefix);
            _prefixPos += fromPrefix;
            return fromPrefix;
        }
        return _rest.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken ct
    )
    {
        int prefixRemaining = _prefixLength - _prefixPos;
        if (prefixRemaining > 0)
        {
            int fromPrefix = Math.Min(count, prefixRemaining);
            Array.Copy(_prefix, _prefixPos, buffer, offset, fromPrefix);
            _prefixPos += fromPrefix;
            return fromPrefix;
        }
        return await _rest.ReadAsync(buffer, offset, count, ct);
    }

    public override void Flush() => _rest.Flush();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
