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
using FluentAssertions;
using NomNomzBot.Infrastructure.Services.Ipc;

namespace NomNomzBot.Infrastructure.Tests.Ipc;

/// <summary>
/// The NDJSON framing layer both production endpoints (Unix socket, named pipe) share: whole
/// frames come out one line at a time regardless of how the bytes arrive, the 64&#160;KB cap is
/// a hard verdict rather than unbounded buffering, and EOF reads as a closed peer.
/// </summary>
public sealed class StreamIpcConnectionTests
{
    [Fact]
    public async Task Pipelined_frames_in_one_buffer_come_out_one_at_a_time()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":2}\r\n"));
        StreamIpcConnection connection = new(stream);

        IpcReadResult first = await connection.ReadFrameAsync(CancellationToken.None);
        first.Frame.Should().Be("{\"a\":1}");

        IpcReadResult second = await connection.ReadFrameAsync(CancellationToken.None);
        second.Frame.Should().Be("{\"b\":2}", "a CR before the delimiter is stripped");

        IpcReadResult third = await connection.ReadFrameAsync(CancellationToken.None);
        third.IsClosed.Should().BeTrue("EOF after the last delimiter is a closed peer");
    }

    [Fact]
    public async Task A_frame_beyond_the_cap_is_a_verdict_not_a_buffer()
    {
        // One byte over the cap, no delimiter in sight — the transport must refuse, not keep reading.
        byte[] oversized = new byte[StreamIpcConnection.MaxFrameBytes + 1];
        Array.Fill(oversized, (byte)'x');
        using MemoryStream stream = new(oversized);
        StreamIpcConnection connection = new(stream);

        IpcReadResult result = await connection.ReadFrameAsync(CancellationToken.None);

        result.FrameTooLarge.Should().BeTrue();
        result.IsClosed.Should().BeFalse();
    }

    [Fact]
    public async Task A_frame_exactly_at_the_cap_still_parses()
    {
        // Cap bytes of payload + the delimiter: legal, because the cap bounds the frame itself.
        string payload = new('y', StreamIpcConnection.MaxFrameBytes);
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(payload + "\n"));
        StreamIpcConnection connection = new(stream);

        IpcReadResult result = await connection.ReadFrameAsync(CancellationToken.None);

        result.Frame.Should().HaveLength(StreamIpcConnection.MaxFrameBytes);
    }

    [Fact]
    public async Task Truncated_partial_line_at_EOF_is_a_closed_peer_not_a_frame()
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes("{\"unterminated\":true}"));
        StreamIpcConnection connection = new(stream);

        IpcReadResult result = await connection.ReadFrameAsync(CancellationToken.None);

        result.IsClosed.Should().BeTrue("a line without its delimiter is not a frame");
    }

    [Fact]
    public async Task Write_appends_the_newline_delimiter()
    {
        using MemoryStream stream = new();
        StreamIpcConnection connection = new(stream);

        await connection.WriteFrameAsync("{\"ok\":true}", CancellationToken.None);
        await connection.WriteFrameAsync("{\"ok\":false}", CancellationToken.None);

        Encoding.UTF8.GetString(stream.ToArray()).Should().Be("{\"ok\":true}\n{\"ok\":false}\n");
    }
}
