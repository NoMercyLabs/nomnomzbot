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
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// S-SR-STALE-3: proves (or disproves) that the WebSocket receive loop's buffer reuse, or the
/// notification→translator handoff, can cross two <c>channel.chat.message</c> frames — the shape that would
/// make <c>!sr</c> answer with the PREVIOUS request's text (owner's live report: "!sr joliene" then
/// "!sr 9 to 5" replied with Jolene twice). This drives the REAL <see cref="WebSocketEventSubTransport"/>
/// receive loop (the production code reuses one <c>byte[64*1024]</c> across every <c>ReceiveAsync</c> call —
/// <see cref="ScriptedChannel"/> below writes into that SAME array on each call, exactly like the real
/// <c>ClientWebSocket</c> would) and the REAL <see cref="ChannelChatMessageTranslator"/>, wired through a sink
/// that mirrors <c>TwitchEventSubHostedService.OnNotificationAsync</c>'s shape (skip the tenant resolver — not
/// under test here, already proven elsewhere — but keep every other step: JsonElement in, translator out,
/// domain event published).
/// </summary>
public sealed class WebSocketEventSubTransportChatOrderingTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static string Welcome(string sessionId) =>
        "{\"metadata\":{\"message_id\":\"w-"
        + sessionId
        + "\",\"message_type\":\"session_welcome\",\"message_timestamp\":\"2026-06-20T12:00:00Z\"},"
        + "\"payload\":{\"session\":{\"id\":\""
        + sessionId
        + "\",\"status\":\"connected\",\"keepalive_timeout_seconds\":30}}}";

    private static string ChatFrame(string messageId, string chatterLogin, string text) =>
        $$$$"""
            {"metadata":{"message_id":"{{{{messageId}}}}","message_type":"notification","message_timestamp":"2026-06-20T12:02:00Z"},
             "payload":{"subscription":{"id":"sub-chat","type":"channel.chat.message","version":"1","status":"enabled"},
                        "event":{"broadcaster_user_id":"twitch-9","chatter_user_id":"555",
                                 "chatter_user_login":"{{{{chatterLogin}}}}","chatter_user_name":"{{{{chatterLogin}}}}",
                                 "message_id":"{{{{messageId}}}}","message_type":"text",
                                 "message":{"text":"{{{{text}}}}","fragments":[{"type":"text","text":"{{{{text}}}}"}]},
                                 "badges":[]}}}
            """;

    private static string UnrelatedFollowFrame() =>
        """
            {"metadata":{"message_id":"n-follow","message_type":"notification","message_timestamp":"2026-06-20T12:02:10Z"},
             "payload":{"subscription":{"id":"sub-follow","type":"channel.follow","version":"2","status":"enabled"},
                        "event":{"broadcaster_user_id":"twitch-9","user_id":"42"}}}
            """;

    private static WebSocketEventSubTransport NewTransport(
        ScriptedChannel channel,
        FakeTimeProvider clock,
        ChatCapturingSink sink
    )
    {
        WebSocketEventSubTransport transport = new(
            new SingleChannelFactory(channel),
            new SingleServiceScopeFactory(new NoopHelixTransport()),
            new EventSubConditionBuilder(),
            clock,
            NullLogger<WebSocketEventSubTransport>.Instance
        );
        transport.BindSink(sink);
        return transport;
    }

    [Fact]
    public async Task TwoChatFrames_BackToBack_EachProducesItsOwnText_NoLag()
    {
        // "!sr joliene" then "!sr 9 to 5" — back to back, no delay, exactly as they arrived in the owner's chat.
        FakeTimeProvider clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingEventBus bus = new();
        ChatCapturingSink sink = new(bus);
        ScriptedChannel channel = new([
            Welcome("session-A"),
            ChatFrame("m-1", "owner", "!sr joliene"),
            ChatFrame("m-2", "owner", "!sr 9 to 5"),
        ]);
        WebSocketEventSubTransport transport = NewTransport(channel, clock, sink);

        await transport.StartAsync();
        await WaitForCountAsync(bus, 2);

        List<ChatMessageReceivedEvent> received = [.. bus.EventsOf<ChatMessageReceivedEvent>()];
        received.Should().HaveCount(2);
        received[0].MessageId.Should().Be("m-1");
        received[0]
            .Message.Should()
            .Be("!sr joliene", "the first frame's own text must reach the first event");
        received[1].MessageId.Should().Be("m-2");
        received[1]
            .Message.Should()
            .Be(
                "!sr 9 to 5",
                "the second frame's own text must reach the second event — a one-behind bug would leave "
                    + "this still reading '!sr joliene' from the reused receive buffer"
            );

        await transport.StopAsync();
    }

    [Fact]
    public async Task TwoChatFrames_WithAnUnrelatedNotificationBetweenThem_StillEachKeepTheirOwnText()
    {
        FakeTimeProvider clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));
        CapturingEventBus bus = new();
        ChatCapturingSink sink = new(bus);
        ScriptedChannel channel = new([
            Welcome("session-A"),
            ChatFrame("m-1", "owner", "!sr joliene"),
            UnrelatedFollowFrame(),
            ChatFrame("m-2", "owner", "!sr 9 to 5"),
        ]);
        WebSocketEventSubTransport transport = NewTransport(channel, clock, sink);

        await transport.StartAsync();
        await WaitForCountAsync(bus, 2);

        List<ChatMessageReceivedEvent> received = [.. bus.EventsOf<ChatMessageReceivedEvent>()];
        received.Should().HaveCount(2);
        received[0].Message.Should().Be("!sr joliene");
        received[1].Message.Should().Be("!sr 9 to 5");

        await transport.StopAsync();
    }

    private static async Task WaitForCountAsync(CapturingEventBus bus, int count)
    {
        for (int i = 0; i < 200 && bus.EventsOf<ChatMessageReceivedEvent>().Count() < count; i++)
            await Task.Delay(25);
        bus.EventsOf<ChatMessageReceivedEvent>()
            .Count()
            .Should()
            .Be(
                count,
                "the expected number of chat events should have been published within the timeout"
            );
    }

    /// <summary>
    /// Mirrors the shape of <c>TwitchEventSubHostedService.OnNotificationAsync</c> for the one topic under test:
    /// takes the JsonElement handed up from the real receive loop, builds the same <see cref="EventSubNotification"/>
    /// the production dispatcher would, and routes <c>channel.chat.message</c> straight into the REAL
    /// <see cref="ChannelChatMessageTranslator"/> (skipping the journal/dedupe/tenant-resolver layers — already
    /// proven safe by prior S-SR-STALE slices; this test isolates the transport → translator handoff only).
    /// </summary>
    private sealed class ChatCapturingSink(CapturingEventBus bus) : IEventSubNotificationSink
    {
        private readonly ChannelChatMessageTranslator _translator = new(
            bus,
            new FakeTimeProvider(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)),
            Substitute.For<IChannelRegistry>(),
            NeverSuppressGuard()
        );

        private static IBotSelfEchoGuard NeverSuppressGuard()
        {
            IBotSelfEchoGuard guard = Substitute.For<IBotSelfEchoGuard>();
            guard
                .ShouldSuppressAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(false);
            return guard;
        }

        public Task OnSessionWelcomeAsync(
            string sessionId,
            string ownerKey,
            CancellationToken ct
        ) => Task.CompletedTask;

        public async Task OnNotificationAsync(
            string messageId,
            DateTimeOffset messageTimestamp,
            string subscriptionType,
            string subscriptionVersion,
            string twitchBroadcasterUserId,
            JsonElement @event,
            CancellationToken ct
        )
        {
            if (subscriptionType != "channel.chat.message")
                return;

            EventSubNotification notification = new()
            {
                MessageId = messageId,
                MessageTimestamp = messageTimestamp,
                SubscriptionType = subscriptionType,
                SubscriptionVersion = subscriptionVersion,
                BroadcasterId = Tenant,
                TwitchBroadcasterUserId = twitchBroadcasterUserId,
                Event = @event,
            };

            await _translator.TranslateAsync(notification, ct);
        }

        public Task OnRevocationAsync(
            string twitchSubscriptionId,
            string subscriptionType,
            string status,
            string twitchBroadcasterUserId,
            CancellationToken ct
        ) => Task.CompletedTask;

        public Task OnSessionDisconnectedAsync(
            string ownerKey,
            string? sessionId,
            string reason,
            TimeSpan nextRetryIn,
            CancellationToken ct
        ) => Task.CompletedTask;
    }

    /// <summary>A single-channel factory — every connect returns the same scripted channel.</summary>
    private sealed class SingleChannelFactory(ScriptedChannel channel) : IWebSocketChannelFactory
    {
        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.FromResult<IWebSocketChannel>(channel);
    }

    /// <summary>
    /// An in-memory <see cref="IWebSocketChannel"/> that yields a fixed script of frames over the SAME
    /// caller-supplied buffer each call — precisely how <see cref="WebSocketEventSubTransport"/>'s production
    /// receive loop drives a real <c>ClientWebSocket</c> (one <c>byte[64*1024]</c> allocated once, reused for
    /// every <c>ReceiveAsync</c>). Then goes idle so the keepalive timer — not a scripted close — ends the run.
    /// </summary>
    private sealed class ScriptedChannel(IReadOnlyList<string> frames) : IWebSocketChannel
    {
        private readonly Queue<string> _frames = new(frames);
        private readonly TaskCompletionSource _idle = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken
        )
        {
            if (_frames.Count > 0)
            {
                byte[] payload = Encoding.UTF8.GetBytes(_frames.Dequeue());
                payload.CopyTo(buffer.Array!, buffer.Offset);
                return Task.FromResult(
                    new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Text, true)
                );
            }

            return WaitIdleAsync(cancellationToken);
        }

        private async Task<WebSocketReceiveResult> WaitIdleAsync(
            CancellationToken cancellationToken
        )
        {
            await using (cancellationToken.Register(() => _idle.TrySetResult()))
                await _idle.Task;
            return new(0, WebSocketMessageType.Close, true);
        }

        public ValueTask DisposeAsync()
        {
            _idle.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A no-op Helix transport — these tests exercise the receive loop, not subscription HTTP.</summary>
    private sealed class NoopHelixTransport : ITwitchHelixTransport
    {
        public Task<Result<T>> GetSingleAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<T>("not used", "NOT_FOUND"));

        public Task<Result<IReadOnlyList<T>>> GetListAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<T>>([]));

        public Task<Result<TwitchPage<T>>> GetPageAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new TwitchPage<T>([], null, 0)));

        public Task<Result<int>> GetTotalAsync(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(0));

        public Task<Result<string>> GetRawAsync(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(""));

        public Task<Result> SendAsync(TwitchHelixRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<T>> SendWithResultAsync<T>(
            TwitchHelixRequest request,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Failure<T>("not used", "NOT_FOUND"));
    }

    /// <summary>
    /// A real <see cref="IServiceScopeFactory"/> over a one-binding container, mirroring how the singleton
    /// transport resolves the scoped Helix client per call in production.
    /// </summary>
    private sealed class SingleServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public SingleServiceScopeFactory(ITwitchHelixTransport helix) =>
            _provider = new ServiceCollection()
                .AddScoped<ITwitchHelixTransport>(_ => helix)
                .BuildServiceProvider();

        public IServiceScope CreateScope() => _provider.CreateScope();
    }
}
