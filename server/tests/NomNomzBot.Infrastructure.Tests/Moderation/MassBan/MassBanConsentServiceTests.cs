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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation.MassBan;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.MassBan;

/// <summary>
/// Proves the mass-ban rule (chat-client.md §3.5): the operator's own channel, the attacked channel and every
/// offline channel ban at once; only a live channel is held — its chat is told when the bot is there, and
/// <c>!allow massban</c> / <c>!disallow massban</c> decide its held batches. An excluded channel and a channel
/// whose owner opted out get no batch at all, and the preview shows the same verdicts the request acts on.
/// </summary>
public sealed class MassBanConsentServiceTests
{
    private static readonly MassBanTarget[] Targets =
    [
        new("1001", "follow-bot storm on qtkitte, followed 2026-10-05T19:02Z"),
        new("1002", "follow-bot storm on qtkitte, followed 2026-10-05T19:02Z"),
    ];

    private static MassBanScope Scope(
        IReadOnlyCollection<string>? attacked = null,
        IReadOnlyCollection<string>? excluded = null
    ) => new(attacked ?? [], excluded ?? []);

    [Fact]
    public async Task Own_attacked_and_offline_channels_ban_at_once_only_a_live_channel_is_held()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney", live: true);
        world.ServedChannel("k1", "qtkitte", live: true);
        world.ServedChannel("o1", "offline_served");
        world.UnservedChannel("o2", "offline_unserved");
        Guid liveServed = world.ServedChannel("l1", "live_served", live: true);
        world.UnservedChannel("l2", "live_unserved", live: true);

        Result<MassBanSweepResult> result = await world
            .Consent()
            .RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, Scope(["qtkitte"]));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Channels.ToDictionary(c => c.BroadcasterLogin, c => c.Status)
            .Should()
            .Equal(
                new Dictionary<string, string>
                {
                    ["stoney"] = MassBanChannelStatus.Banning,
                    ["qtkitte"] = MassBanChannelStatus.Banning,
                    ["offline_served"] = MassBanChannelStatus.Banning,
                    ["offline_unserved"] = MassBanChannelStatus.Banning,
                    ["live_served"] = MassBanChannelStatus.AwaitingApproval,
                    ["live_unserved"] = MassBanChannelStatus.HeldUntilOffline,
                }
            );
        result.Value.Channels.Should().OnlyContain(c => c.Accounts == 2);

        List<MassBanBatch> batches = await world
            .Db.MassBanBatches.Include(b => b.Targets)
            .ToListAsync();
        batches.Should().HaveCount(6, "one batch per moderated channel");
        batches
            .Should()
            .OnlyContain(b => b.Targets.Count == 2 && b.OperatorDisplayName == "Stoney");
        batches
            .Where(b => b.ChannelLogin is "stoney" or "qtkitte")
            .Should()
            .OnlyContain(b => !b.HoldWhileLive, "the own and attacked channels never wait");
        batches
            .Where(b => b.ChannelLogin is not ("stoney" or "qtkitte"))
            .Should()
            .OnlyContain(b => b.HoldWhileLive, "every other channel waits while live");
        batches.Single(b => b.ChannelLogin == "live_served").NoticeSentAt.Should().NotBeNull();
        batches.Single(b => b.ChannelLogin == "live_served").ChannelId.Should().Be(liveServed);
        batches.Single(b => b.ChannelLogin == "live_unserved").ChannelId.Should().BeNull();
        batches
            .Where(b => b.ChannelLogin != "live_served")
            .Should()
            .OnlyContain(b => b.NoticeSentAt == null, "only a live, served channel is told");

        await world
            .Chat.Received(1)
            .SendMessageAsync(liveServed, "line:request", Arg.Any<CancellationToken>());
        BuiltinResponseRequest notice = (BuiltinResponseRequest)
            world.Composer.ReceivedCalls().Single().GetArguments()[0]!;
        notice.BuiltinKey.Should().Be(BuiltinResponseSlots.MassBan.Key);
        notice.Variables.Should().Contain("massban.moderator", "Stoney");
        notice.Variables.Should().Contain("massban.count", "2");
        notice.Variables.Should().Contain("massban.channel", "live_served");
    }

    [Fact]
    public async Task Excluded_and_opted_out_channels_get_no_batch_and_the_preview_says_so()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        world.ServedChannel("x1", "left_out");
        world.ServedChannel("n1", "said_no", accepts: false);
        world.ServedChannel("y1", "takes_part");

        MassBanConsentService consent = world.Consent();
        MassBanScope scope = Scope(excluded: ["LEFT_OUT"]);
        Result<IReadOnlyList<MassBanChannelPreview>> preview = await consent.PreviewAsync(
            MassBanTestWorld.Operator,
            scope
        );
        Result<MassBanSweepResult> result = await consent.RequestAsync(
            MassBanTestWorld.Operator,
            "Stoney",
            Targets,
            scope
        );

        preview.IsSuccess.Should().BeTrue(preview.ErrorMessage);
        preview
            .Value.Select(p => (p.BroadcasterLogin, p.Status))
            .Should()
            .Equal(
                ("stoney", MassBanChannelStatus.Banning),
                ("left_out", MassBanChannelStatus.Excluded),
                ("said_no", MassBanChannelStatus.OptedOut),
                ("takes_part", MassBanChannelStatus.Banning)
            );
        preview.Value.Single(p => p.BroadcasterLogin == "stoney").IsOwnChannel.Should().BeTrue();

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Channels.Select(c => (c.BroadcasterLogin, c.Status, c.Accounts))
            .Should()
            .Equal(
                ("stoney", MassBanChannelStatus.Banning, 2),
                ("left_out", MassBanChannelStatus.Excluded, 0),
                ("said_no", MassBanChannelStatus.OptedOut, 0),
                ("takes_part", MassBanChannelStatus.Banning, 2)
            );
        (await world.Db.MassBanBatches.Select(b => b.ChannelLogin).ToListAsync())
            .Should()
            .BeEquivalentTo(["stoney", "takes_part"]);
    }

    [Fact]
    public async Task A_live_served_channel_whose_chat_refuses_the_notice_is_held_until_offline()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        world.ServedChannel("l1", "live_served", live: true);
        world
            .Chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        Result<MassBanSweepResult> result = await world
            .Consent()
            .RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, Scope());

        result
            .Value.Channels.Single(c => c.BroadcasterLogin == "live_served")
            .Status.Should()
            .Be(MassBanChannelStatus.HeldUntilOffline);
        (await world.Db.MassBanBatches.SingleAsync(b => b.ChannelLogin == "live_served"))
            .NoticeSentAt.Should()
            .BeNull();
    }

    [Fact]
    public async Task Duplicates_are_dropped_and_a_bad_list_is_refused_before_any_batch_exists()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        MassBanConsentService consent = world.Consent();

        Result<MassBanSweepResult> deduped = await consent.RequestAsync(
            MassBanTestWorld.Operator,
            "Stoney",
            [new("1001", "a"), new("1001", "b"), new("1002", new string('r', 600))],
            Scope()
        );
        Result<MassBanSweepResult> empty = await consent.RequestAsync(
            MassBanTestWorld.Operator,
            "Stoney",
            [],
            Scope()
        );
        Result<MassBanSweepResult> notNumeric = await consent.RequestAsync(
            MassBanTestWorld.Operator,
            "Stoney",
            [new("qtkitte", "a login is not an id")],
            Scope()
        );

        deduped.IsSuccess.Should().BeTrue(deduped.ErrorMessage);
        deduped.Value.Channels.Single().Accounts.Should().Be(2);
        List<MassBanBatchTarget> stored = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .OrderBy(t => t.TwitchUserId)
            .ToListAsync();
        stored.Select(t => t.TwitchUserId).Should().Equal("1001", "1002");
        stored[1].Reason.Should().HaveLength(500, "Twitch takes at most 500 characters");

        empty.IsFailure.Should().BeTrue();
        empty.ErrorCode.Should().Be("VALIDATION_FAILED");
        notNumeric.IsFailure.Should().BeTrue();
        notNumeric.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await world.Db.MassBanBatches.CountAsync())
            .Should()
            .Be(1, "a refused list creates nothing");
    }

    [Fact]
    public async Task When_Twitch_cannot_say_who_is_live_the_request_fails_and_nothing_is_queued()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        world.ServedChannel("l1", "someone");
        world
            .Streams.GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchStream>>("Helix is down.", "UPSTREAM"));

        Result<MassBanSweepResult> result = await world
            .Consent()
            .RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, Scope());

        result.IsFailure.Should().BeTrue("a guess about who is live could ban into a live stream");
        result.ErrorCode.Should().Be("UPSTREAM");
        (await world.Db.MassBanBatches.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Allow_approves_only_the_held_batches_of_that_channel_and_counts_what_is_left()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        Guid live = world.ServedChannel("l1", "live_served", live: true);
        Guid other = world.ServedChannel("l2", "other_live", live: true);
        MassBanConsentService consent = world.Consent();
        await consent.RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, Scope());
        MassBanBatch held = await world
            .Db.MassBanBatches.Include(b => b.Targets)
            .SingleAsync(b => b.ChannelId == live);
        held.Targets[0].ProcessedAt = world.Clock.GetUtcNow().UtcDateTime;
        await world.Db.SaveChangesAsync();
        world.Db.ChangeTracker.Clear();

        MassBanDecision first = await consent.ApproveAsync(live, "live_served");
        MassBanDecision again = await consent.ApproveAsync(live, "live_served");
        MassBanDecision ownChannel = await consent.ApproveAsync(world.OwnChannelId, "stoney");

        first.Should().Be(new MassBanDecision(MassBanDecisionStatus.Approved, 1));
        again
            .Status.Should()
            .Be(MassBanDecisionStatus.NothingPending, "a decided batch is not open");
        ownChannel
            .Status.Should()
            .Be(MassBanDecisionStatus.NothingPending, "the own channel never held anything");
        MassBanBatch approved = await world.Db.MassBanBatches.SingleAsync(b => b.ChannelId == live);
        approved.ApprovedAt.Should().Be(world.Clock.GetUtcNow().UtcDateTime);
        approved.DecidedByDisplayName.Should().Be("live_served");
        (await world.Db.MassBanBatches.SingleAsync(b => b.ChannelId == other))
            .ApprovedAt.Should()
            .BeNull("another channel's answer is not this channel's answer");
    }

    [Fact]
    public async Task Disallow_declines_the_held_batch_so_the_executor_never_touches_it()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        Guid live = world.ServedChannel("l1", "live_served", live: true);
        MassBanConsentService consent = world.Consent();
        await consent.RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, Scope());

        MassBanDecision decision = await consent.DeclineAsync(live, "live_served");
        world.GoOffline("l1");
        int banned = await world.Executor().RunAsync(maxBans: 50);

        decision.Should().Be(new MassBanDecision(MassBanDecisionStatus.Declined, 2));
        (await world.Db.MassBanBatches.SingleAsync(b => b.ChannelId == live))
            .DeclinedAt.Should()
            .NotBeNull();
        banned.Should().Be(2, "only the own channel's batch ran");
        await world
            .Moderation.DidNotReceive()
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                "l1",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
