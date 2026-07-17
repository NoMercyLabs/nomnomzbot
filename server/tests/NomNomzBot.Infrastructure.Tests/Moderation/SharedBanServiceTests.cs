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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the shared-ban trust web CRUD (moderation.md §3.5, J.9/J.9a): a channel with no row reads as the
/// safe defaults (both OFF, empty list); saves upsert; trust adds are idempotent and refuse self/unknown
/// channels; removes are real deletes with NOT_FOUND on absence; and EVERY write re-verifies the SuperMod
/// floor in-process — a Moderator(10) actor is refused with state untouched even though the HTTP gate passed.
/// </summary>
public sealed class SharedBanServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ba01");
    private static readonly Guid Partner = Guid.Parse("0192a000-0000-7000-8000-00000000ba02");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000ba03");

    private static (
        SharedBanService Sut,
        ModerationServiceTestDbContext Db,
        SharedChatSessionTracker Sessions,
        ITwitchModerationApi Twitch
    ) Build(int actorLevel = 20)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        IRoleResolver roles = Substitute.For<IRoleResolver>();
        roles
            .ResolveEffectiveLevelAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(actorLevel));
        SharedChatSessionTracker sessions = new();
        ITwitchModerationApi twitch = Substitute.For<ITwitchModerationApi>();
        twitch
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchBanResult("b", "b", "troll-42", DateTimeOffset.UnixEpoch, null)
                )
            );
        return (new SharedBanService(db, roles, sessions, twitch), db, sessions, twitch);
    }

    private static async Task SeedChannelsAsync(ModerationServiceTestDbContext db)
    {
        db.Channels.Add(NewChannel(Channel, "trusting_chan"));
        db.Channels.Add(NewChannel(Partner, "partner_chan"));
        await db.SaveChangesAsync();
    }

    private static Channel NewChannel(Guid id, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = id.ToString("N")[..8],
            Name = name,
            NameNormalized = name,
        };

    [Fact]
    public async Task A_channel_with_no_row_reads_as_the_safe_defaults()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> result = await sut.GetSettingsAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.AcceptSharedChatBans.Should().BeFalse("opt-in, default-deny");
        result.Value.ShareOutgoingBans.Should().BeFalse();
        result.Value.TrustedChannels.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_upserts_the_policy_and_a_second_save_updates_the_same_row()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> first = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(AcceptSharedChatBans: true, ShareOutgoingBans: false)
        );
        first.Value.AcceptSharedChatBans.Should().BeTrue();

        Result<SharedBanSettingsDto> second = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(AcceptSharedChatBans: false, ShareOutgoingBans: true)
        );
        second.Value.AcceptSharedChatBans.Should().BeFalse();
        second.Value.ShareOutgoingBans.Should().BeTrue();

        (await db.SharedBanSettings.CountAsync(s => s.BroadcasterId == Channel))
            .Should()
            .Be(1, "the save is an upsert, never a second row");
    }

    [Fact]
    public async Task Add_trusted_channel_persists_and_readds_idempotently()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);

        Result<SharedBanTrustedChannelDto> added = await sut.AddTrustedChannelAsync(
            Channel,
            Actor,
            Partner
        );
        added.IsSuccess.Should().BeTrue(added.ErrorMessage);
        added.Value.TrustedChannelName.Should().Be("partner_chan");
        added.Value.AddedByUserId.Should().Be(Actor);

        // Re-adding the same partner is a no-op returning the existing row — never a duplicate.
        Result<SharedBanTrustedChannelDto> again = await sut.AddTrustedChannelAsync(
            Channel,
            Actor,
            Partner
        );
        again.IsSuccess.Should().BeTrue(again.ErrorMessage);
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(1);

        // The settings read carries the trust list.
        (await sut.GetSettingsAsync(Channel))
            .Value.TrustedChannels.Should()
            .ContainSingle(t => t.TrustedChannelId == Partner);
    }

    [Fact]
    public async Task Add_refuses_self_trust_and_unknown_channels()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);

        (await sut.AddTrustedChannelAsync(Channel, Actor, Channel))
            .ErrorCode.Should()
            .Be("VALIDATION_FAILED");
        (await sut.AddTrustedChannelAsync(Channel, Actor, Guid.NewGuid()))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    [Fact]
    public async Task Remove_deletes_the_entry_and_absence_is_not_found()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build();
        await SeedChannelsAsync(db);
        await sut.AddTrustedChannelAsync(Channel, Actor, Partner);

        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner)).IsSuccess.Should().BeTrue();
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(0);

        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("NOT_FOUND");
    }

    // ─── Inbound apply (the trust predicate + the ban itself) ────────────────

    private static SharedChatBanIssuedEvent Inbound(string sessionId = "session-1") =>
        new()
        {
            BroadcasterId = Partner,
            SharedChatSessionId = sessionId,
            OriginChannelId = Partner, // the PARTNER issued the ban; Channel decides whether to apply
            TargetTwitchUserId = "troll-42",
            TargetDisplayName = "Troll",
            Reason = "spam",
        };

    private static async Task OptInAndTrustAsync(SharedBanService sut)
    {
        await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(AcceptSharedChatBans: true, ShareOutgoingBans: false)
        );
        await sut.AddTrustedChannelAsync(Channel, Actor, Partner);
    }

    [Fact]
    public async Task Inbound_apply_bans_records_provenance_when_the_full_predicate_holds()
    {
        (
            SharedBanService sut,
            ModerationServiceTestDbContext db,
            SharedChatSessionTracker sessions,
            ITwitchModerationApi twitch
        ) = Build();
        await SeedChannelsAsync(db);
        await OptInAndTrustAsync(sut);
        sessions.SetSession(Channel, new SharedChatSessionInfo("session-1", "host-1", ["a", "b"]));

        Result<SharedBanApplicationResult> result = await sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Applied.Should().BeTrue(result.Value.SkippedReason);
        result.Value.ActionId.Should().NotBeNull();

        // The Twitch ban ran on the PARTNER channel's own tenant token.
        await twitch
            .Received(1)
            .BanUserAsync(Channel, "troll-42", "spam", Arg.Any<CancellationToken>());

        // The provenance row: same moderation_action record type, carrying WHERE the ban came from.
        Domain.Platform.Entities.Record record = await db.Records.SingleAsync(r =>
            r.BroadcasterId == Channel && r.RecordType == "moderation_action"
        );
        record.Data.Should().Contain("\"Origin\":\"shared_chat\"");
        record.Data.Should().Contain(Partner.ToString());
        record.Data.Should().Contain("troll-42");
    }

    [Fact]
    public async Task Inbound_apply_skips_without_acceptance_trust_or_a_matching_session()
    {
        (
            SharedBanService sut,
            ModerationServiceTestDbContext db,
            SharedChatSessionTracker sessions,
            ITwitchModerationApi twitch
        ) = Build();
        await SeedChannelsAsync(db);

        // 1. No settings row (accept defaults to OFF).
        (await sut.ApplyInboundSharedBanAsync(Channel, Inbound()))
            .Value.SkippedReason.Should()
            .Be("not_accepting");

        // 2. Accepting, but the origin is not on the trust list.
        await sut.SaveSettingsAsync(Channel, Actor, new SaveSharedBanSettingsRequest(true, false));
        (await sut.ApplyInboundSharedBanAsync(Channel, Inbound()))
            .Value.SkippedReason.Should()
            .Be("origin_not_trusted");

        // 3. Trusted, but the partner is not in that shared-chat session right now.
        await sut.AddTrustedChannelAsync(Channel, Actor, Partner);
        (await sut.ApplyInboundSharedBanAsync(Channel, Inbound()))
            .Value.SkippedReason.Should()
            .Be("no_shared_session");
        sessions.SetSession(Channel, new SharedChatSessionInfo("OTHER-session", "host-1", []));
        (await sut.ApplyInboundSharedBanAsync(Channel, Inbound()))
            .Value.SkippedReason.Should()
            .Be("no_shared_session");

        // No predicate ever passed — Twitch was never called, nothing recorded.
        await twitch
            .DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        (await db.Records.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Inbound_apply_reports_a_twitch_failure_without_recording()
    {
        (
            SharedBanService sut,
            ModerationServiceTestDbContext db,
            SharedChatSessionTracker sessions,
            ITwitchModerationApi twitch
        ) = Build();
        await SeedChannelsAsync(db);
        await OptInAndTrustAsync(sut);
        sessions.SetSession(Channel, new SharedChatSessionInfo("session-1", "host-1", []));
        twitch
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("missing scope", "TWITCH_MISSING_SCOPE"));

        Result<SharedBanApplicationResult> result = await sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.Value.Applied.Should().BeFalse();
        result.Value.SkippedReason.Should().Be("twitch_ban_failed:TWITCH_MISSING_SCOPE");
        (await db.Records.CountAsync())
            .Should()
            .Be(0, "no ban happened, so nothing may be recorded");
    }

    [Fact]
    public async Task Writes_refuse_an_actor_below_the_supermod_floor_and_touch_nothing()
    {
        (SharedBanService sut, ModerationServiceTestDbContext db, _, _) = Build(actorLevel: 10); // Moderator
        await SeedChannelsAsync(db);

        Result<SharedBanSettingsDto> save = await sut.SaveSettingsAsync(
            Channel,
            Actor,
            new SaveSharedBanSettingsRequest(true, true)
        );
        save.ErrorCode.Should().Be("FORBIDDEN");
        (await sut.AddTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("FORBIDDEN");
        (await sut.RemoveTrustedChannelAsync(Channel, Actor, Partner))
            .ErrorCode.Should()
            .Be("FORBIDDEN");

        (await db.SharedBanSettings.CountAsync()).Should().Be(0);
        (await db.SharedBanTrustedChannels.CountAsync()).Should().Be(0);

        // Reads are NOT floor-gated in-service (the read gate is the HTTP policy).
        (await sut.GetSettingsAsync(Channel))
            .IsSuccess.Should()
            .BeTrue();
    }
}
