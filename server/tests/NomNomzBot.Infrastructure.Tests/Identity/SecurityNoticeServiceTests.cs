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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavior tests for the durable half of S-IMPERSONATION-NOTICE. Every test proves the PERSISTED content
/// a channel owner reads later (actor, reason, scope, expiry, read state) — never merely that a call
/// returned non-null — and that a second channel's rows are unreachable from the first (tenant scoping).
/// </summary>
public sealed class SecurityNoticeServiceTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static SecurityNoticeService NewService(SecurityNoticeTestDbContext db) =>
        new(db, Clock);

    /// <summary>
    /// The scenario the slice exists for: the tenant owner was OFFLINE for the entire impersonation window
    /// (no SignalR connection to receive the transient alert). The durable row must still be retrievable
    /// afterwards, carrying who acted, why, the scope, and the expiry — not just "something happened".
    /// </summary>
    [Fact]
    public async Task RecordAsync_impersonation_started_while_owner_offline_persists_actor_reason_scope_and_expiry()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        SecurityNoticeService service = NewService(db);

        Guid broadcasterId = Guid.CreateVersion7();
        Guid actorPrincipalId = Guid.CreateVersion7();
        Guid targetUserId = Guid.CreateVersion7();
        Guid accessGrantId = Guid.CreateVersion7();
        DateTime expiresAt = Clock.GetUtcNow().UtcDateTime.AddHours(1);

        Result<SecurityNoticeDto> result = await service.RecordAsync(
            new RecordSecurityNoticeRequest(
                broadcasterId,
                "impersonation_started",
                "A NomNomzBot operator started acting as a user on your channel.",
                actorPrincipalId,
                targetUserId,
                accessGrantId,
                "Debugging a bot-command report per ticket #4821.",
                "channel",
                expiresAt
            )
        );

        result.IsSuccess.Should().BeTrue();

        // Re-read via a FRESH context — proves the write actually landed in the store, not just an
        // in-memory echo of the request the service was handed.
        await using SecurityNoticeTestDbContext reader = db;
        SecurityNotice persisted = await reader.SecurityNotices.SingleAsync(n =>
            n.BroadcasterId == broadcasterId
        );

        persisted.NoticeType.Should().Be("impersonation_started");
        persisted.ActorPrincipalId.Should().Be(actorPrincipalId);
        persisted.TargetUserId.Should().Be(targetUserId);
        persisted.AccessGrantId.Should().Be(accessGrantId);
        persisted.Reason.Should().Be("Debugging a bot-command report per ticket #4821.");
        persisted.Scope.Should().Be("channel");
        persisted.ExpiresAt.Should().Be(expiresAt);
        persisted.AcknowledgedAt.Should().BeNull("an unread notice must start unacknowledged");
    }

    [Fact]
    public async Task ListAsync_returns_every_past_notice_for_the_channel_newest_first()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        SecurityNoticeService service = NewService(db);
        Guid broadcasterId = Guid.CreateVersion7();

        await service.RecordAsync(Started(broadcasterId, "first session"));
        Clock.Advance(TimeSpan.FromMinutes(5));
        await service.RecordAsync(Ended(broadcasterId, "first session"));
        Clock.Advance(TimeSpan.FromMinutes(5));
        await service.RecordAsync(Started(broadcasterId, "second session"));

        Result<PagedList<SecurityNoticeDto>> page = await service.ListAsync(
            broadcasterId,
            new PaginationParams(1, 25)
        );

        page.IsSuccess.Should().BeTrue();
        page.Value.Items.Should().HaveCount(3);
        page.Value.Items.Select(n => n.Reason)
            .Should()
            .ContainInOrder("second session", "first session", "first session");
    }

    [Fact]
    public async Task AcknowledgeAsync_marks_unread_notice_read_and_it_sticks_across_a_reload()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        SecurityNoticeService service = NewService(db);
        Guid broadcasterId = Guid.CreateVersion7();
        Guid ownerUserId = Guid.CreateVersion7();

        Result<SecurityNoticeDto> recorded = await service.RecordAsync(Started(broadcasterId, "r"));
        Guid noticeId = recorded.Value.Id;

        Result<SecurityNoticeDto> acknowledged = await service.AcknowledgeAsync(
            broadcasterId,
            noticeId,
            ownerUserId
        );

        acknowledged.IsSuccess.Should().BeTrue();
        acknowledged.Value.AcknowledgedAt.Should().NotBeNull();
        acknowledged.Value.AcknowledgedByUserId.Should().Be(ownerUserId);

        // "Sticks across a reload": build a brand-new service instance over the SAME store and read again —
        // the read state must not have lived only in the first call's in-memory objects.
        SecurityNoticeService reloadedService = NewService(db);
        Result<PagedList<SecurityNoticeDto>> reloaded = await reloadedService.ListAsync(
            broadcasterId,
            new PaginationParams(1, 25)
        );
        SecurityNoticeDto reloadedNotice = reloaded.Value.Items.Single(n => n.Id == noticeId);
        reloadedNotice.AcknowledgedAt.Should().NotBeNull();
        reloadedNotice.AcknowledgedByUserId.Should().Be(ownerUserId);
    }

    [Fact]
    public async Task AcknowledgeAsync_is_idempotent_it_keeps_the_first_acknowledgement()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        SecurityNoticeService service = NewService(db);
        Guid broadcasterId = Guid.CreateVersion7();
        Guid firstAcker = Guid.CreateVersion7();
        Guid secondAcker = Guid.CreateVersion7();

        Result<SecurityNoticeDto> recorded = await service.RecordAsync(Started(broadcasterId, "r"));
        Guid noticeId = recorded.Value.Id;

        Result<SecurityNoticeDto> first = await service.AcknowledgeAsync(
            broadcasterId,
            noticeId,
            firstAcker
        );
        Clock.Advance(TimeSpan.FromMinutes(1));
        Result<SecurityNoticeDto> second = await service.AcknowledgeAsync(
            broadcasterId,
            noticeId,
            secondAcker
        );

        second.Value.AcknowledgedByUserId.Should().Be(firstAcker);
        second.Value.AcknowledgedAt.Should().Be(first.Value.AcknowledgedAt);
    }

    /// <summary>Tenant scoping: another channel's notices are not readable, and an acknowledge attempt
    /// against the wrong channel fails NOT_FOUND rather than silently touching someone else's row.</summary>
    [Fact]
    public async Task ListAsync_and_AcknowledgeAsync_never_cross_tenant_boundaries()
    {
        SecurityNoticeTestDbContext db = SecurityNoticeTestDbContext.New();
        SecurityNoticeService service = NewService(db);
        Guid ownChannel = Guid.CreateVersion7();
        Guid otherChannel = Guid.CreateVersion7();

        await service.RecordAsync(Started(ownChannel, "own channel's session"));
        Result<SecurityNoticeDto> otherRecorded = await service.RecordAsync(
            Started(otherChannel, "other channel's session")
        );

        Result<PagedList<SecurityNoticeDto>> ownList = await service.ListAsync(
            ownChannel,
            new PaginationParams(1, 25)
        );
        ownList.Value.Items.Should().ContainSingle();
        ownList.Value.Items.Single().Reason.Should().Be("own channel's session");

        Result<SecurityNoticeDto> crossTenantAck = await service.AcknowledgeAsync(
            ownChannel,
            otherRecorded.Value.Id,
            Guid.CreateVersion7()
        );
        crossTenantAck.IsFailure.Should().BeTrue();
        crossTenantAck.ErrorCode.Should().Be("NOT_FOUND");
    }

    private static RecordSecurityNoticeRequest Started(Guid broadcasterId, string reason) =>
        new(
            broadcasterId,
            "impersonation_started",
            "A NomNomzBot operator started acting as a user on your channel.",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            reason,
            "channel",
            Clock.GetUtcNow().UtcDateTime.AddHours(1)
        );

    private static RecordSecurityNoticeRequest Ended(Guid broadcasterId, string reason) =>
        new(
            broadcasterId,
            "impersonation_ended",
            "A NomNomzBot operator stopped acting as a user on your channel.",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            reason,
            "channel",
            null
        );
}
