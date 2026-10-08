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
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves a shared ban that is refused or skipped is REPORTED, never a quiet "skipped" success: the apply path
/// returns a failure carrying the reason, and (for every reason the streamer can act on) publishes a
/// <see cref="SharedChatBanNotAppliedEvent"/> for the channel that should have banned. A channel that never
/// opted in to accepting bans is the one deliberate silence (default-deny is a choice, not a fault).
/// </summary>
public sealed class SharedBanNotAppliedTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000be01");
    private static readonly Guid Origin = Guid.Parse("0192a000-0000-7000-8000-00000000be02");

    private sealed record Rig(
        SharedBanService Sut,
        ModerationServiceTestDbContext Db,
        SharedChatSessionTracker Sessions,
        ITwitchModerationApi Twitch,
        RecordingEventBus Bus
    );

    private static async Task<Rig> BuildAsync(bool accept, bool trustOrigin)
    {
        ModerationServiceTestDbContext db = ModerationServiceTestDbContext.New();
        db.SharedBanSettings.Add(new() { BroadcasterId = Channel, AcceptSharedChatBans = accept });
        if (trustOrigin)
            db.SharedBanTrustedChannels.Add(
                new() { BroadcasterId = Channel, TrustedChannelId = Origin }
            );
        await db.SaveChangesAsync();

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
        RecordingEventBus bus = new();
        SharedBanService sut = new(db, Substitute.For<IRoleResolver>(), sessions, twitch, bus);
        return new(sut, db, sessions, twitch, bus);
    }

    private static SharedChatBanIssuedEvent Inbound() =>
        new()
        {
            BroadcasterId = Origin,
            SharedChatSessionId = "session-1",
            OriginChannelId = Origin,
            TargetTwitchUserId = "troll-42",
            TargetDisplayName = "Troll",
            Reason = "spam",
        };

    [Fact]
    public async Task A_ban_Twitch_refuses_fails_with_the_reason_and_reports_it_for_the_channel()
    {
        Rig rig = await BuildAsync(accept: true, trustOrigin: true);
        rig.Sessions.SetSession(Channel, new("session-1", "host-1", []));
        rig.Twitch.BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("missing scope", "TWITCH_MISSING_SCOPE"));

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.IsFailure.Should().BeTrue("a refused ban must never read as a success");
        result.ErrorCode.Should().Be("twitch_ban_failed");
        SharedChatBanNotAppliedEvent reported = rig
            .Bus.Published.OfType<SharedChatBanNotAppliedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        reported.BroadcasterId.Should().Be(Channel);
        reported.Reason.Should().Be("twitch_ban_failed");
        reported.Origin.Should().Be("shared_chat");
        reported.OriginChannelId.Should().Be(Origin);
        reported.TargetTwitchUserId.Should().Be("troll-42");
        reported.TargetDisplayName.Should().Be("Troll");
        reported.Detail.Should().Contain("TWITCH_MISSING_SCOPE").And.Contain("missing scope");
        (await rig.Db.Records.CountAsync()).Should().Be(0, "no ban happened, nothing is recorded");
    }

    [Fact]
    public async Task A_ban_with_no_shared_session_fails_and_is_reported_not_counted_as_applied()
    {
        Rig rig = await BuildAsync(accept: true, trustOrigin: true);

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("no_shared_session");
        rig.Bus.Published.OfType<SharedChatBanNotAppliedEvent>()
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be("no_shared_session");
        await rig
            .Twitch.DidNotReceive()
            .BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_ban_from_an_untrusted_origin_fails_and_is_reported_to_the_accepting_channel()
    {
        Rig rig = await BuildAsync(accept: true, trustOrigin: false);
        rig.Sessions.SetSession(Channel, new("session-1", "host-1", []));

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.ErrorCode.Should().Be("origin_not_trusted");
        rig.Bus.Published.OfType<SharedChatBanNotAppliedEvent>()
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be("origin_not_trusted");
    }

    [Fact]
    public async Task A_channel_that_never_opted_in_fails_the_apply_but_reports_nothing()
    {
        Rig rig = await BuildAsync(accept: false, trustOrigin: false);

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("not_accepting");
        rig.Bus.Published.Should()
            .BeEmpty("default-deny is the channel's own choice, not something to nag about");
    }

    [Fact]
    public async Task A_federated_ban_Twitch_refuses_fails_and_is_reported_with_the_federation_origin()
    {
        Rig rig = await BuildAsync(accept: false, trustOrigin: false);
        rig.Twitch.BanUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchBanResult>("already banned", "TWITCH_CONFLICT"));

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundFederatedBanAsync(
            Channel,
            Inbound()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("twitch_ban_failed");
        SharedChatBanNotAppliedEvent reported = rig
            .Bus.Published.OfType<SharedChatBanNotAppliedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        reported.BroadcasterId.Should().Be(Channel);
        reported.Origin.Should().Be("federation");
        reported.Detail.Should().Contain("TWITCH_CONFLICT");
    }

    [Fact]
    public async Task A_ban_that_applies_reports_nothing_and_carries_the_action_id()
    {
        Rig rig = await BuildAsync(accept: true, trustOrigin: true);
        rig.Sessions.SetSession(Channel, new("session-1", "host-1", []));

        Result<SharedBanApplicationResult> result = await rig.Sut.ApplyInboundSharedBanAsync(
            Channel,
            Inbound()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.ActionId.Should().BeGreaterThan(0);
        rig.Bus.Published.OfType<SharedChatBanNotAppliedEvent>().Should().BeEmpty();
    }
}
