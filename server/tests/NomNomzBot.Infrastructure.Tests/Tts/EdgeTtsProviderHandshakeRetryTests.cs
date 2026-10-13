// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.WebSockets;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Microsoft refuses the read-aloud handshake with 403 at random (eight probes in one Sec-MS-GEC window
/// gave 101, 101, 403, 403, 101, 101...). One try meant a silent shoutout; the provider must retry a 403
/// on a fresh socket and give up at once on any other failure.
/// </summary>
public sealed class EdgeTtsProviderHandshakeRetryTests
{
    private static WebSocketException Refused403() =>
        new(
            WebSocketError.NotAWebSocket,
            "The server returned status code '403' when status code '101' was expected."
        );

    private sealed class ScriptedHandshake
    {
        private readonly Queue<Exception?> _outcomes;

        public ScriptedHandshake(params Exception?[] outcomes) => _outcomes = new(outcomes);

        public List<Uri> Urls { get; } = [];
        public List<ClientWebSocket> Sockets { get; } = [];

        public Task Connect(ClientWebSocket ws, Uri url, CancellationToken ct)
        {
            Urls.Add(url);
            Sockets.Add(ws);
            Exception? outcome = _outcomes.Dequeue();
            return outcome is null ? Task.CompletedTask : Task.FromException(outcome);
        }

        public string ConnectionId(int index) =>
            HttpUtility.ParseQueryString(Urls[index].Query)["ConnectionId"]!;

        public string Gec(int index) =>
            HttpUtility.ParseQueryString(Urls[index].Query)["Sec-MS-GEC"]!;
    }

    private static EdgeTtsProvider CreateProvider(
        FakeTimeProvider time,
        ScriptedHandshake script
    ) =>
        new(
            time,
            Substitute.For<IHttpClientFactory>(),
            NullLogger<EdgeTtsProvider>.Instance,
            script.Connect
        );

    /// <summary>Runs the task while stepping the fake clock, so the retry delay costs no real time.</summary>
    private static async Task<T> RunWithClock<T>(FakeTimeProvider time, Task<T> task)
    {
        while (!task.IsCompleted)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Yield();
        }
        return await task;
    }

    [Fact]
    public async Task Connect_403Then403ThenSuccess_ReturnsASocketAfterThreeFreshHandshakes()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-13T10:00:00Z"));
        ScriptedHandshake script = new(Refused403(), Refused403(), null);
        EdgeTtsProvider provider = CreateProvider(time, script);

        EdgeTtsProvider.EdgeConnection? connection = await RunWithClock(
            time,
            provider.ConnectWithRetryAsync(CancellationToken.None)
        );

        connection.Should().NotBeNull();
        script.Urls.Should().HaveCount(3);
        script.Sockets.Distinct().Should().HaveCount(3, "every try must use a new ClientWebSocket");
        List<string> ids = [script.ConnectionId(0), script.ConnectionId(1), script.ConnectionId(2)];
        ids.Distinct().Should().HaveCount(3, "every try must carry its own ConnectionId");
        connection!.ConnectionId.Should().Be(ids[2]);
        connection.Socket.Should().BeSameAs(script.Sockets[2]);
        connection.Socket.Dispose();
    }

    [Fact]
    public async Task Synthesize_403OnAllThreeTries_ReturnsEmptyResultAfterExactlyThreeConnects()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-13T10:00:00Z"));
        ScriptedHandshake script = new(Refused403(), Refused403(), Refused403());
        EdgeTtsProvider provider = CreateProvider(time, script);

        TtsSynthesisResult result = await RunWithClock(
            time,
            provider.SynthesizeAsync("hello", "en-US-AriaNeural")
        );

        script.Urls.Should().HaveCount(3);
        result.AudioData.Should().BeEmpty();
        result.DurationMs.Should().Be(0);
        result.VoiceId.Should().Be("en-US-AriaNeural");
    }

    [Fact]
    public async Task Connect_NonForbiddenFailure_IsNotRetried()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-13T10:00:00Z"));
        ScriptedHandshake script = new(
            new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "reset")
        );
        EdgeTtsProvider provider = CreateProvider(time, script);

        EdgeTtsProvider.EdgeConnection? connection = await provider.ConnectWithRetryAsync(
            CancellationToken.None
        );

        connection.Should().BeNull();
        script.Urls.Should().HaveCount(1);
    }

    [Fact]
    public async Task Connect_CancelledDuringTheRetryDelay_StopsAtOnce()
    {
        FakeTimeProvider time = new(DateTimeOffset.Parse("2026-10-13T10:00:00Z"));
        ScriptedHandshake script = new(Refused403(), null);
        EdgeTtsProvider provider = CreateProvider(time, script);
        using CancellationTokenSource cts = new();

        Task<EdgeTtsProvider.EdgeConnection?> task = provider.ConnectWithRetryAsync(cts.Token);
        await cts.CancelAsync();

        Func<Task> act = async () => await task;
        await act.Should().ThrowAsync<OperationCanceledException>();
        script.Urls.Should().HaveCount(1, "no second handshake after cancellation");
    }
}
