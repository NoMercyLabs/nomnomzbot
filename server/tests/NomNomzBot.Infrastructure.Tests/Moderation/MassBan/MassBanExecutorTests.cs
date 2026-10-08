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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Infrastructure.Moderation.MassBan;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation.MassBan;

/// <summary>
/// Proves the executor carries a batch out the way the rule says: a held batch waits while its channel is live
/// and runs once it is offline or approved; every ban rides the requesting moderator's token; progress is
/// stamped per target so a restart resumes; an already-banned account counts as banned; and only a chat that
/// was told about the batch hears how it ended.
/// </summary>
public sealed class MassBanExecutorTests
{
    private static readonly MassBanTarget[] Targets =
    [
        new("1001", "storm"),
        new("1002", "storm"),
        new("1003", "storm"),
    ];

    private static async Task QueueAsync(MassBanTestWorld world) =>
        (
            await world
                .Consent()
                .RequestAsync(MassBanTestWorld.Operator, "Stoney", Targets, new([], []))
        )
            .IsSuccess.Should()
            .BeTrue();

    [Fact]
    public async Task A_held_batch_waits_while_live_and_runs_once_the_stream_ends()
    {
        MassBanTestWorld world = new();
        world.UnservedChannel("l1", "live_unserved", live: true);
        world.ModeratorOptIn("l1", "live_unserved");
        await QueueAsync(world);

        int whileLive = await world.Executor().RunAsync(maxBans: 50);
        world.GoOffline("l1");
        int afterStream = await world.Executor().RunAsync(maxBans: 50);

        whileLive.Should().Be(0, "a live channel is not disrupted");
        afterStream.Should().Be(3);
        MassBanBatch batch = await world.Db.MassBanBatches.Include(b => b.Targets).SingleAsync();
        batch.CompletedAt.Should().Be(world.Clock.GetUtcNow().UtcDateTime);
        batch
            .Targets.Should()
            .OnlyContain(t => t.Banned && t.ProcessedAt != null && t.Error == null);
        foreach (string id in Targets.Select(t => t.TwitchUserId))
            await world
                .Moderation.Received(1)
                .BanAsOperatorAsync(
                    MassBanTestWorld.Operator,
                    "l1",
                    id,
                    "storm",
                    Arg.Any<CancellationToken>()
                );
    }

    [Fact]
    public async Task A_channel_whose_owner_opted_in_bans_with_its_own_token_when_the_operator_is_no_twitch_mod()
    {
        // Owner 2026-10-06 (anda_six): "meaning you use his account" — yes. Twitch does not list the operator as
        // a moderator there, so the operator's token cannot ban; the channel joined the bot and its owner opted
        // in, so the batch runs under the broadcaster's own token. Only a roster moderator or a platform principal
        // reaches a channel this way; a stranger gets nothing.
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        Guid rostered = world.OwnerOptedInChannelWithoutTwitchMod(
            "r1",
            "on_roster",
            onRoster: true
        );
        world.OwnerOptedInChannelWithoutTwitchMod("s1", "stranger", onRoster: false);
        await QueueAsync(world);

        int banned = await world.Executor().RunAsync(maxBans: 50);
        banned += await world.Executor().RunAsync(maxBans: 50);

        banned.Should().Be(6, "the own channel and the rostered channel, three accounts each");
        List<MassBanBatch> batches = await world.Db.MassBanBatches.ToListAsync();
        batches.Select(b => b.ChannelLogin).Should().BeEquivalentTo(["stoney", "on_roster"]);
        MassBanBatch viaOwner = batches.Single(b => b.ChannelLogin == "on_roster");
        viaOwner.RunsAsBroadcaster.Should().BeTrue();
        viaOwner.ChannelId.Should().Be(rostered);
        batches.Single(b => b.ChannelLogin == "stoney").RunsAsBroadcaster.Should().BeFalse();
        await world
            .Moderation.Received(3)
            .BanUserAsync(rostered, Arg.Any<string>(), "storm", Arg.Any<CancellationToken>());
        await world
            .Moderation.DidNotReceive()
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                "r1",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_platform_principal_reaches_an_owner_opted_in_channel_without_a_roster_row()
    {
        MassBanTestWorld world = new();
        world.OperatorIsPlatformPrincipal();
        world.OwnChannel("own1", "stoney");
        Guid channel = world.OwnerOptedInChannelWithoutTwitchMod("a1", "anda", onRoster: false);
        await QueueAsync(world);

        await world.Executor().RunAsync(maxBans: 50);
        await world.Executor().RunAsync(maxBans: 50);

        await world
            .Moderation.Received(3)
            .BanUserAsync(channel, Arg.Any<string>(), "storm", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_approved_batch_runs_while_the_channel_is_still_live()
    {
        MassBanTestWorld world = new();
        Guid live = world.ServedChannel("l1", "live_served", live: true);
        await QueueAsync(world);
        await world.Consent().ApproveAsync(live, "live_served");

        int banned = await world.Executor().RunAsync(maxBans: 50);

        banned.Should().Be(3);
    }

    [Fact]
    public async Task When_Twitch_cannot_say_who_is_live_held_batches_keep_waiting()
    {
        MassBanTestWorld world = new();
        world.UnservedChannel("l1", "someone");
        await QueueAsync(world);
        world
            .Streams.GetStreamsAsync(
                Arg.Any<TwitchStreamsFilter>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchStream>>("Helix is down.", "UPSTREAM"));

        int banned = await world.Executor().RunAsync(maxBans: 50);

        banned.Should().Be(0, "not knowing is treated as live");
    }

    [Fact]
    public async Task Runs_in_slices_stamps_each_target_and_resumes_where_it_stopped()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);

        int first = await world.Executor().RunAsync(maxBans: 2);
        List<MassBanBatchTarget> afterFirst = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .OrderBy(t => t.TwitchUserId)
            .ToListAsync();
        DateTime? completedAfterFirst = (await world.Db.MassBanBatches.SingleAsync()).CompletedAt;
        world.Db.ChangeTracker.Clear();
        int second = await world.Executor().RunAsync(maxBans: 2);
        int third = await world.Executor().RunAsync(maxBans: 2);

        first.Should().Be(2);
        afterFirst.Count(t => t.ProcessedAt != null).Should().Be(2);
        completedAfterFirst.Should().BeNull("one target is still unprocessed");
        second.Should().Be(1, "only the unprocessed target is left");
        third.Should().Be(0, "a completed batch is not picked up again");
        await world
            .Moderation.Received(3)
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_already_banned_account_counts_as_banned_and_a_refusal_is_recorded_per_target()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);
        world
            .Moderation.BanAsOperatorAsync(
                Arg.Any<Guid>(),
                "own1",
                "1002",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                // Live 2026-10-06: the Helix transport says "Twitch request failed (400)." and keeps Twitch's own
                // sentence in the detail; 60 already-banned accounts were recorded as refusals.
                Result.Failure<TwitchBanResult>(
                    "Twitch request failed (400).",
                    "TWITCH_ERROR",
                    "{\"error\":\"Bad Request\",\"status\":400,\"message\":\"The user specified in the user_id field is already banned.\"}"
                )
            );
        world
            .Moderation.BanAsOperatorAsync(
                Arg.Any<Guid>(),
                "own1",
                "1003",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>(
                    "Twitch request failed (403).",
                    "FORBIDDEN",
                    "Missing scope."
                )
            );

        await world.Executor().RunAsync(maxBans: 50);

        Dictionary<string, MassBanBatchTarget> targets = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .ToDictionaryAsync(t => t.TwitchUserId);
        targets["1001"].Banned.Should().BeTrue();
        targets["1002"].Banned.Should().BeTrue("Twitch already has the ban we wanted");
        targets["1003"].Banned.Should().BeFalse();
        targets["1003"]
            .Error.Should()
            .Be("Twitch request failed (403). Missing scope.", "the detail names the cause");
        targets.Values.Should().OnlyContain(t => t.ProcessedAt != null, "a refusal is not retried");
    }

    private static void Answer(
        MassBanTestWorld world,
        string targetId,
        params Result<TwitchBanResult>[] answers
    ) =>
        world
            .Moderation.BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                targetId,
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(answers[0], answers[1..]);

    private static Result<TwitchBanResult> Banned(string targetId) =>
        Result.Success(
            new TwitchBanResult("own1", "mod", targetId, DateTimeOffset.UnixEpoch, null)
        );

    [Fact]
    public async Task A_rate_limited_target_waits_for_its_backoff_and_is_banned_on_a_later_pass()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);
        Answer(
            world,
            "1002",
            Result.Failure<TwitchBanResult>(
                "Twitch request failed (429).",
                TwitchErrorCodes.RateLimited,
                "Too Many Requests"
            ),
            Banned("1002")
        );

        await world.Executor().RunAsync(maxBans: 50);
        world.Db.ChangeTracker.Clear();
        MassBanBatchTarget afterFirst = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .SingleAsync(t => t.TwitchUserId == "1002");
        int tooEarly = await world.Executor().RunAsync(maxBans: 50);
        world.Clock.Advance(TimeSpan.FromMinutes(5));
        world.Db.ChangeTracker.Clear();
        int onRetry = await world.Executor().RunAsync(maxBans: 50);

        afterFirst.ProcessedAt.Should().BeNull("Twitch never answered for this account");
        afterFirst.Banned.Should().BeFalse();
        afterFirst.Attempts.Should().Be(1);
        afterFirst.Error.Should().Contain("429");
        afterFirst
            .NextAttemptAt.Should()
            .BeAfter(world.Clock.GetUtcNow().UtcDateTime.AddMinutes(-5));
        tooEarly.Should().Be(0, "the backoff has not passed");
        onRetry.Should().Be(1);
        world.Db.ChangeTracker.Clear();
        MassBanBatch batch = await world.Db.MassBanBatches.Include(b => b.Targets).SingleAsync();
        MassBanBatchTarget retried = batch.Targets.Single(t => t.TwitchUserId == "1002");
        retried.Banned.Should().BeTrue();
        retried.ProcessedAt.Should().Be(world.Clock.GetUtcNow().UtcDateTime);
        retried.Attempts.Should().Be(2);
        retried.Error.Should().BeNull("the success clears the earlier failure");
        retried.NextAttemptAt.Should().BeNull();
        batch.CompletedAt.Should().NotBeNull("every target has an answer now");
    }

    [Fact]
    public async Task A_target_that_keeps_failing_transiently_is_marked_failed_after_the_last_attempt()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);
        world
            .Moderation.BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                "1003",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchBanResult>("Twitch request failed (503).", "twitch_error")
            );

        for (int pass = 0; pass < MassBanRetryPolicy.MaxAttempts + 1; pass++)
        {
            await world.Executor().RunAsync(maxBans: 50);
            world.Clock.Advance(TimeSpan.FromHours(1));
            world.Db.ChangeTracker.Clear();
        }

        MassBanBatchTarget gaveUp = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .SingleAsync(t => t.TwitchUserId == "1003");
        gaveUp.Attempts.Should().Be(MassBanRetryPolicy.MaxAttempts);
        gaveUp.Banned.Should().BeFalse();
        gaveUp.ProcessedAt.Should().NotBeNull();
        gaveUp.NextAttemptAt.Should().BeNull();
        gaveUp.Error.Should().Contain("503").And.Contain("Gave up");
    }

    [Fact]
    public async Task A_permanent_refusal_stores_its_reason_and_is_not_retried()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);
        Answer(
            world,
            "1001",
            Result.Failure<TwitchBanResult>(
                "Twitch request failed (400).",
                "twitch_error",
                "The user specified in the user_id field may not be banned."
            )
        );

        await world.Executor().RunAsync(maxBans: 50);
        world.Clock.Advance(TimeSpan.FromHours(1));
        world.Db.ChangeTracker.Clear();
        int again = await world.Executor().RunAsync(maxBans: 50);

        MassBanBatchTarget refused = await world
            .Db.MassBanBatches.SelectMany(b => b.Targets)
            .SingleAsync(t => t.TwitchUserId == "1001");
        refused.Banned.Should().BeFalse();
        refused.ProcessedAt.Should().NotBeNull();
        refused.Attempts.Should().Be(1);
        refused.NextAttemptAt.Should().BeNull();
        refused.Error.Should().Contain("may not be banned");
        again.Should().Be(0, "a refusal is final");
        await world
            .Moderation.Received(1)
            .BanAsOperatorAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                "1001",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_target_is_stamped_processed_only_after_Twitch_answered()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        await QueueAsync(world);
        List<DateTime?> stampDuringCall = [];
        world
            .Moderation.When(m =>
                m.BanAsOperatorAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call =>
                stampDuringCall.Add(
                    world
                        .Db.ChangeTracker.Entries<MassBanBatchTarget>()
                        .Single(e => e.Entity.TwitchUserId == call.ArgAt<string>(2))
                        .Entity.ProcessedAt
                )
            );

        await world.Executor().RunAsync(maxBans: 50);

        stampDuringCall.Should().HaveCount(3).And.OnlyContain(stamp => stamp == null);
        (await world.Db.MassBanBatches.SelectMany(b => b.Targets).ToListAsync())
            .Should()
            .OnlyContain(t => t.ProcessedAt != null && t.Banned);
    }

    [Fact]
    public async Task Only_a_chat_that_was_told_about_the_batch_hears_the_closing_line()
    {
        MassBanTestWorld world = new();
        world.OwnChannel("own1", "stoney");
        Guid live = world.ServedChannel("l1", "live_served", live: true);
        await QueueAsync(world);
        world.Chat.ClearReceivedCalls();
        world.Composer.ClearReceivedCalls();
        world.GoOffline("l1");

        await world.Executor().RunAsync(maxBans: 50);
        await world.Executor().RunAsync(maxBans: 50);

        (await world.Db.MassBanBatches.CountAsync(b => b.CompletedAt != null)).Should().Be(2);
        await world
            .Chat.Received(1)
            .SendMessageAsync(live, "line:completed", Arg.Any<CancellationToken>());
        await world
            .Chat.DidNotReceive()
            .SendMessageAsync(world.OwnChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        BuiltinResponseRequest closing = (BuiltinResponseRequest)
            world.Composer.ReceivedCalls().Single().GetArguments()[0]!;
        closing.Variables.Should().Contain("massban.banned", "3");
        closing.Variables.Should().Contain("massban.count", "3");
    }

    [Fact]
    public async Task Every_ban_runs_under_the_requesting_moderators_sanction()
    {
        // Live 2026-10-06: the worker has no HTTP request, so Helix refused all 497 bans of the first
        // batch as "not sanctioned". The moderator asked for them through moderation:ban; that is the basis.
        MassBanTestWorld world = new();
        world.OwnChannel("o1", "own");
        await QueueAsync(world);
        List<OutboundSanction?> observed = [];
        world
            .Moderation.When(m =>
                m.BanAsOperatorAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string?>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => observed.Add(world.Sanctions.Current));

        await world.Executor().RunAsync(maxBans: 50);

        observed.Should().HaveCount(3);
        observed
            .Should()
            .AllSatisfy(s =>
            {
                s.Should().NotBeNull();
                s.Basis.Should().Be(OutboundSanctionBasis.UserAction);
                s.Detail.Should().Be("moderation:ban");
                s.ActorUserId.Should().Be(MassBanTestWorld.Operator);
            });
        world.Sanctions.Current.Should().BeNull("the scope ends with the run");
    }
}
