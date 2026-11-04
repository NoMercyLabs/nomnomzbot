// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Overlays.Services;
using NomNomzBot.Infrastructure.Overlays;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Overlays;

/// <summary>
/// Proves the generic overlay feed's source (widgets-overlays.md): every journaled event that carries a tenant and
/// is not encrypted is forwarded verbatim (type + raw payload) to that channel's overlay feed; a tenant-less or
/// encrypted event is never pushed to a browser source; and a hub-push failure is swallowed so the commit path is
/// never disturbed. Assertions are on the collaborator actually driven.
/// </summary>
public sealed class OverlayEventFeedHookTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-4444-7000-8000-000000000001");

    private static (OverlayEventFeedHook Hook, IOverlayEventFeed Feed) Build(
        IHeldTextService? heldText = null
    )
    {
        IOverlayEventFeed feed = Substitute.For<IOverlayEventFeed>();
        return (
            new(
                feed,
                Substitute.For<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry>(),
                NullLogger<OverlayEventFeedHook>.Instance,
                heldText
            ),
            feed
        );
    }

    [Theory]
    [InlineData(true, "AutomaticRewardRedeemedEvent", "Message")]
    [InlineData(false, "AutomaticRewardRedeemedEvent", "Message")]
    [InlineData(true, "BitsUsedEvent", "MessageText")]
    [InlineData(false, "BitsUsedEvent", "MessageText")]
    [InlineData(true, "CustomPowerUpRedeemedEvent", "UserInput")]
    [InlineData(false, "CustomPowerUpRedeemedEvent", "UserInput")]
    public async Task Raw_viewer_text_event_reaches_overlay_only_when_input_is_released(
        bool held,
        string eventType,
        string textKey
    )
    {
        IHeldTextService heldText = Substitute.For<IHeldTextService>();
        heldText
            .IsTextHeldAsync(
                Tenant,
                "viewer-1",
                "viewer secret",
                Arg.Any<CancellationToken>(),
                Arg.Any<DateTimeOffset?>()
            )
            .Returns(held);
        heldText
            .IsChatLineHeldAsync(
                Tenant,
                "viewer-1",
                "viewer secret",
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(held);
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build(heldText);
        string payload = $"{{\"UserId\":\"viewer-1\",\"{textKey}\":\"viewer secret\"}}";

        await hook.OnCommittedAsync(Record(Tenant, eventType, payload));

        if (held)
            feed.ReceivedCalls().Should().BeEmpty();
        else
            await feed.Received(1)
                .BroadcastEventAsync(Tenant, eventType, payload, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("ChatUserMessageHeldEvent")]
    [InlineData("AutoModMessageHeldEvent")]
    [InlineData("ChatUserMessageUpdatedEvent")]
    public async Task Raw_moderation_review_events_never_reach_public_overlays(string eventType)
    {
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();

        await hook.OnCommittedAsync(
            Record(Tenant, eventType, "{\"UserId\":\"viewer-1\",\"Text\":\"viewer secret\"}")
        );

        feed.ReceivedCalls().Should().BeEmpty();
    }

    private static EventRecord Record(
        Guid? broadcasterId,
        // A still-forwarded user-facing event (now-playing has no decorated re-broadcast) — the alert/chat events
        // are dropped by OverlayEventFilter now that their dashboard handler re-emits them decorated.
        string eventType = "PlaybackStateChangedEvent",
        string payloadJson = "{\"message\":\"hi\"}",
        bool encrypted = false
    ) =>
        new(
            Id: 1,
            EventId: Guid.Parse("019f2a00-4444-7000-8000-0000000000aa"),
            BroadcasterId: broadcasterId,
            StreamPosition: 1,
            EventType: eventType,
            EventVersion: 1,
            Source: "twitch",
            PayloadJson: payloadJson,
            PayloadIsEncrypted: encrypted,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: default,
            RecordedAt: default
        );

    [Fact]
    public async Task OnCommitted_TenantEvent_ForwardsTypeAndPayloadToFeed()
    {
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();

        Result result = await hook.OnCommittedAsync(
            Record(Tenant, "PlaybackStateChangedEvent", "{\"isPlaying\":true}")
        );

        result.IsSuccess.Should().BeTrue();
        await feed.Received(1)
            .BroadcastEventAsync(
                Tenant,
                "PlaybackStateChangedEvent",
                "{\"isPlaying\":true}",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnCommitted_ChatMessageEvent_IsNotForwarded_BecauseReBroadcastDecorated()
    {
        // The raw ChatMessageReceivedEvent has no render data; it reaches overlays instead as the decorated
        // "ChatMessage" event (ChatMessageBroadcastHandler), so the generic feed must NOT forward the raw form
        // (OverlayEventFilter drops it) — a widget would otherwise get a useless duplicate it can't render.
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();

        Result result = await hook.OnCommittedAsync(Record(Tenant, "ChatMessageReceivedEvent"));

        result.IsSuccess.Should().BeTrue();
        await feed.DidNotReceive()
            .BroadcastEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnCommitted_EncryptedPayload_IsNotPushed()
    {
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();

        await hook.OnCommittedAsync(Record(Tenant, encrypted: true));

        await feed.DidNotReceive()
            .BroadcastEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnCommitted_NoTenant_IsNotPushed()
    {
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();

        await hook.OnCommittedAsync(Record(broadcasterId: null));
        await hook.OnCommittedAsync(Record(broadcasterId: Guid.Empty));

        await feed.DidNotReceive()
            .BroadcastEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnCommitted_FeedThrows_IsSwallowed()
    {
        (OverlayEventFeedHook hook, IOverlayEventFeed feed) = Build();
        feed.BroadcastEventAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => throw new InvalidOperationException("hub down"));

        Result result = await hook.OnCommittedAsync(Record(Tenant));

        result.IsSuccess.Should().BeTrue();
    }
}
