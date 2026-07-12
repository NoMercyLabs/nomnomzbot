// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
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

    private static (OverlayEventFeedHook Hook, IOverlayEventFeed Feed) Build()
    {
        IOverlayEventFeed feed = Substitute.For<IOverlayEventFeed>();
        return (new OverlayEventFeedHook(feed, NullLogger<OverlayEventFeedHook>.Instance), feed);
    }

    private static EventRecord Record(
        Guid? broadcasterId,
        string eventType = "ChatMessageReceivedEvent",
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
            Record(Tenant, "ChatMessageReceivedEvent", "{\"message\":\"hello overlay\"}")
        );

        result.IsSuccess.Should().BeTrue();
        await feed.Received(1)
            .BroadcastEventAsync(
                Tenant,
                "ChatMessageReceivedEvent",
                "{\"message\":\"hello overlay\"}",
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
            .Returns<Task>(_ => throw new InvalidOperationException("hub down"));

        Result result = await hook.OnCommittedAsync(Record(Tenant));

        result.IsSuccess.Should().BeTrue();
    }
}
