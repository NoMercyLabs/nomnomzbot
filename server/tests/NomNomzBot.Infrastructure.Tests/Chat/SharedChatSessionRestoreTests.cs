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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the shared-chat session survives a restart: the tracker is memory-only, so after a restart the
/// restorer re-reads the channel's active session from Helix (Get Shared Chat Session) and the next shared ban
/// is offered, fanned out to the participating local channels, and applied.
/// </summary>
public sealed class SharedChatSessionRestoreTests
{
    private static readonly Guid Origin = Guid.Parse("0192a000-0000-7000-8000-00000000ce01");
    private static readonly Guid PartnerA = Guid.Parse("0192a000-0000-7000-8000-00000000ce02");
    private static readonly Guid PartnerB = Guid.Parse("0192a000-0000-7000-8000-00000000ce03");
    private static readonly Guid Outsider = Guid.Parse("0192a000-0000-7000-8000-00000000ce04");

    private const string OriginTwitchId = "1001";
    private const string PartnerATwitchId = "1002";
    private const string PartnerBTwitchId = "1003";
    private const string OutsiderTwitchId = "1004";

    private static TwitchSharedChatSession HelixSession() =>
        new(
            "session-9",
            OriginTwitchId,
            [new(OriginTwitchId), new(PartnerATwitchId), new(PartnerBTwitchId)],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch
        );

    private static Channel NewChannel(Guid id, string twitchId, string name) =>
        new()
        {
            Id = id,
            OwnerUserId = Guid.NewGuid(),
            TwitchChannelId = twitchId,
            Name = name,
            NameNormalized = name,
        };

    private static ITwitchChatAssetsApi HelixThatReports(Result<TwitchSharedChatSession> answer)
    {
        ITwitchChatAssetsApi helix = Substitute.For<ITwitchChatAssetsApi>();
        helix
            .GetSharedChatSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(answer);
        return helix;
    }

    private static async Task<ModerationServiceTestDbContext> SeedAsync()
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.Channels.AddRange(
            NewChannel(Origin, OriginTwitchId, "origin_chan"),
            NewChannel(PartnerA, PartnerATwitchId, "partner_a"),
            NewChannel(PartnerB, PartnerBTwitchId, "partner_b"),
            NewChannel(Outsider, OutsiderTwitchId, "outsider")
        );
        db.SharedBanSettings.Add(new() { BroadcasterId = Origin, ShareOutgoingBans = true });
        foreach (Guid partner in new[] { PartnerA, PartnerB })
        {
            db.SharedBanSettings.Add(
                new() { BroadcasterId = partner, AcceptSharedChatBans = true }
            );
            db.SharedBanTrustedChannels.Add(
                new() { BroadcasterId = partner, TrustedChannelId = Origin }
            );
        }
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task AfterARestart_TheSessionIsRestoredFromHelix_AndTheTrackerHoldsIt()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker freshTracker = new();
        ITwitchChatAssetsApi helix = HelixThatReports(Result.Success(HelixSession()));
        SharedChatSessionRestorer sut = new(
            freshTracker,
            helix,
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );

        SharedChatSessionInfo? restored = await sut.EnsureActiveSessionAsync(Origin);

        restored.Should().NotBeNull();
        restored.SessionId.Should().Be("session-9");
        restored.HostBroadcasterId.Should().Be(OriginTwitchId);
        restored
            .ParticipantTwitchIds.Should()
            .Equal(OriginTwitchId, PartnerATwitchId, PartnerBTwitchId);
        freshTracker.GetActiveSession(Origin).Should().Be(restored);
    }

    [Fact]
    public async Task AChannelNotInASession_StaysUntracked()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker tracker = new();
        ITwitchChatAssetsApi helix = HelixThatReports(
            Result.Failure<TwitchSharedChatSession>("no session", TwitchErrorCodes.NotFound)
        );
        SharedChatSessionRestorer sut = new(
            tracker,
            helix,
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );

        (await sut.EnsureActiveSessionAsync(Origin)).Should().BeNull();
        tracker.GetActiveSession(Origin).Should().BeNull();
    }

    [Fact]
    public async Task AHelixFailure_ReturnsNoSession_AndNeverThrows()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker tracker = new();
        ITwitchChatAssetsApi helix = HelixThatReports(
            Result.Failure<TwitchSharedChatSession>("Helix is down", "twitch_unavailable")
        );
        SharedChatSessionRestorer sut = new(
            tracker,
            helix,
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );

        (await sut.EnsureActiveSessionAsync(Origin)).Should().BeNull();
        tracker.GetActiveSession(Origin).Should().BeNull();
    }

    [Fact]
    public async Task ATrackedSession_IsReturnedWithoutCallingHelix()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker tracker = new();
        tracker.SetSession(Origin, new("live-session", "host", []));
        ITwitchChatAssetsApi helix = HelixThatReports(Result.Success(HelixSession()));
        SharedChatSessionRestorer sut = new(
            tracker,
            helix,
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );

        SharedChatSessionInfo? session = await sut.EnsureActiveSessionAsync(Origin);

        session!.SessionId.Should().Be("live-session");
        await helix
            .DidNotReceive()
            .GetSharedChatSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdoptingASession_TracksEveryLocalParticipant_AndLeavesOthersAlone()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker tracker = new();
        SharedChatSessionInfo newer = new("newer-session", "host", []);
        tracker.SetSession(PartnerB, newer);
        SharedChatSessionRestorer sut = new(
            tracker,
            HelixThatReports(Result.Success(HelixSession())),
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );
        SharedChatSessionInfo session = new(
            "session-9",
            OriginTwitchId,
            [OriginTwitchId, PartnerATwitchId, PartnerBTwitchId]
        );

        await sut.AdoptParticipantsAsync(session);

        tracker.GetActiveSession(Origin).Should().Be(session);
        tracker.GetActiveSession(PartnerA).Should().Be(session);
        tracker
            .GetActiveSession(PartnerB)
            .Should()
            .Be(newer, "a tracked session is never overwritten");
        tracker.GetActiveSession(Outsider).Should().BeNull("not a participant");
    }

    [Fact]
    public async Task AfterARestart_TheNextSharedBanIsOffered_FannedOutAndApplied()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker freshTracker = new();
        SharedChatSessionRestorer restorer = new(
            freshTracker,
            HelixThatReports(Result.Success(HelixSession())),
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );
        RecordingEventBus offerBus = new();

        await new SharedChatBanSharePublisher(db, restorer, offerBus).HandleAsync(
            new UserBannedEvent
            {
                BroadcasterId = Origin,
                TargetUserId = "troll-42",
                TargetDisplayName = "Troll",
                ModeratorUserId = "mod-7",
                Reason = "spam",
            }
        );

        SharedChatBanIssuedEvent offered = offerBus
            .Published.OfType<SharedChatBanIssuedEvent>()
            .Should()
            .ContainSingle("the restart must not stop the ban being offered")
            .Subject;
        offered.SharedChatSessionId.Should().Be("session-9");

        ITwitchModerationApi twitch = BanApi();
        SharedBanService service = new(
            db,
            Substitute.For<IRoleResolver>(),
            freshTracker,
            twitch,
            new RecordingEventBus()
        );
        await new SharedChatBanApplyHandler(
            service,
            freshTracker,
            restorer,
            NullLogger<SharedChatBanApplyHandler>.Instance
        ).HandleAsync(offered);

        await twitch
            .Received(1)
            .BanUserAsync(PartnerA, "troll-42", "spam", Arg.Any<CancellationToken>());
        await twitch
            .Received(1)
            .BanUserAsync(PartnerB, "troll-42", "spam", Arg.Any<CancellationToken>());
        await twitch
            .DidNotReceive()
            .BanUserAsync(
                Outsider,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        (await db.Records.CountAsync(r => r.RecordType == "moderation_action"))
            .Should()
            .Be(2, "one provenance row per partner that banned");
    }

    [Fact]
    public async Task OnePartnerRefusing_IsReportedAndNeverBlocksTheOthers()
    {
        ModerationServiceTestDbContext db = await SeedAsync();
        SharedChatSessionTracker tracker = new();
        tracker.SetSession(
            Origin,
            new("session-9", OriginTwitchId, [OriginTwitchId, PartnerATwitchId, PartnerBTwitchId])
        );
        SharedChatSessionRestorer restorer = new(
            tracker,
            HelixThatReports(Result.Success(HelixSession())),
            db,
            NullLogger<SharedChatSessionRestorer>.Instance
        );
        ITwitchModerationApi twitch = BanApi();
        twitch
            .BanUserAsync(
                PartnerA,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("not a moderator", "TWITCH_FORBIDDEN"));
        RecordingEventBus reports = new();
        SharedBanService service = new(
            db,
            Substitute.For<IRoleResolver>(),
            tracker,
            twitch,
            reports
        );

        await new SharedChatBanApplyHandler(
            service,
            tracker,
            restorer,
            NullLogger<SharedChatBanApplyHandler>.Instance
        ).HandleAsync(
            new SharedChatBanIssuedEvent
            {
                BroadcasterId = Origin,
                SharedChatSessionId = "session-9",
                OriginChannelId = Origin,
                TargetTwitchUserId = "troll-42",
                Reason = "spam",
            }
        );

        SharedChatBanNotAppliedEvent reported = reports
            .Published.OfType<SharedChatBanNotAppliedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        reported.BroadcasterId.Should().Be(PartnerA);
        reported.Reason.Should().Be("twitch_ban_failed");
        (await db.Records.CountAsync(r => r.BroadcasterId == PartnerB))
            .Should()
            .Be(1, "partner B still banned");
        (await db.Records.CountAsync(r => r.BroadcasterId == PartnerA)).Should().Be(0);
    }

    private static ITwitchModerationApi BanApi()
    {
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
        return twitch;
    }
}
