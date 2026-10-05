// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// Old-bot parity (ShoutoutQueueService): a manual or raid shoutout inside Twitch's global cooldown waits in a
/// queue and then runs in full; inside only the per-user cooldown it runs at once with the native call
/// skipped. A raid goes before a manual one, a target never waits twice, and the queue dies on a live edge.
/// </summary>
public sealed class ShoutoutQueueTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b302");
    private static readonly TimeSpan GlobalCooldown = TimeSpan.FromMinutes(2);
    private const string ManualId = "111001";
    private const string RaiderId = "222002";

    private sealed class Rig
    {
        public required ShoutoutAction Action { get; init; }
        public required ShoutoutQueueWorker Worker { get; init; }
        public required ShoutoutQueue Queue { get; init; }
        public required ITwitchChatApi Chat { get; init; }
        public required ITtsDispatchService Tts { get; init; }
        public required ChannelContext ChannelCtx { get; init; }
        public required FakeTimeProvider Clock { get; init; }
    }

    private static TwitchUser User(string id) =>
        new(
            Id: id,
            Login: "login" + id,
            DisplayName: "Name" + id,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static string Line(string id) => $"Go check out Name{id} — twitch.tv/login{id}";

    private static async Task<Rig> BuildAsync()
    {
        FakeTimeProvider clock = new(new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>(
                    ((IReadOnlyList<string>)call[0]).Select(User).ToList()
                )
            );

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = (string)call[0];
                foreach (KeyValuePair<string, string> kv in (IDictionary<string, string>)call[1])
                    template = template.Replace("{" + kv.Key + "}", kv.Value);
                return Task.FromResult(template);
            });

        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(TtsDispatchDisposition.Dispatched, "v", "p", 0, 0, null)
                )
            );

        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                Name = "stoney",
                NameNormalized = "stoney",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        ChannelContext channelCtx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-channel",
            ChannelName = "stoney",
        };
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(channelCtx);

        ShoutoutQueue queue = new();
        return new()
        {
            Action = ShoutoutTestFactory.Create(
                chat,
                users,
                registry,
                db,
                resolver,
                tts,
                clock,
                queue
            ),
            Worker = ShoutoutTestFactory.Worker(queue, chat, registry, db, tts, clock),
            Queue = queue,
            Chat = chat,
            Tts = tts,
            ChannelCtx = channelCtx,
            Clock = clock,
        };
    }

    private static PipelineExecutionContext Manual(string userId = ManualId) =>
        Ctx(messageId: "m-" + userId, eventName: null);

    private static PipelineExecutionContext Raid() =>
        Ctx(messageId: string.Empty, eventName: "channel.raid");

    private static PipelineExecutionContext Automated() =>
        Ctx(messageId: string.Empty, eventName: "channel.follow");

    private static PipelineExecutionContext Ctx(string messageId, string? eventName)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-1",
            TriggeredByDisplayName = "viewer",
            MessageId = messageId,
            RawMessage = string.Empty,
        };
        if (eventName is not null)
            ctx.Variables["event.name"] = eventName;
        return ctx;
    }

    private static ActionDefinition Step(string userId, bool tts = true) =>
        new()
        {
            Type = "shoutout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(userId),
                ["tts"] = JsonSerializer.SerializeToElement(tts),
            },
        };

    private static ActionDefinition PriorityStep(string userId, bool priority) =>
        new()
        {
            Type = "shoutout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(userId),
                ["tts"] = JsonSerializer.SerializeToElement(true),
                ["priority"] = JsonSerializer.SerializeToElement(priority),
            },
        };

    private static void StampGlobalCooldown(Rig rig) =>
        rig.ChannelCtx.LastGlobalShoutout = rig.Clock.GetUtcNow();

    private static Task AssertNothingSentAsync(Rig rig) =>
        Task.WhenAll(
            rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!),
            rig.Chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default),
            rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!)
        );

    private static Task AssertSpokenForAsync(Rig rig, string id) =>
        rig
            .Tts.Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r => r.RequestedByTwitchUserId == id && r.Text == Line(id)),
                Arg.Any<CancellationToken>()
            );

    private static Task AssertAnnouncedAsync(Rig rig, string id) =>
        rig
            .Chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                Line(id),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task A_raid_inside_the_global_cooldown_waits_then_runs_in_full_when_the_cooldown_passes()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Raid(), Step(RaiderId, tts: true));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("queued (global cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(RaiderId);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, RaiderId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, RaiderId);
        await AssertSpokenForAsync(rig, RaiderId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_queued_shoutout_does_not_run_before_the_global_cooldown_has_passed()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown - TimeSpan.FromSeconds(1));
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel).Should().NotBeNull();
    }

    [Fact]
    public async Task A_manual_shoutout_inside_the_global_cooldown_waits_then_runs_in_full()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Output.Should().Be("queued (global cooldown)");
        await AssertNothingSentAsync(rig);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
    }

    [Fact]
    public async Task A_manual_shoutout_inside_only_the_per_user_cooldown_announces_and_speaks_at_once_without_the_native_call()
    {
        Rig rig = await BuildAsync();
        rig.ChannelCtx.LastShoutoutPerUser[ManualId] = rig.Clock.GetUtcNow();

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().NotContain("queued");
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_manual_shoutout_inside_both_cooldowns_waits_then_announces_and_speaks_without_the_native_call()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        rig.ChannelCtx.LastShoutoutPerUser[ManualId] = rig.Clock.GetUtcNow();

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Output.Should().Be("queued (global cooldown)");
        await AssertNothingSentAsync(rig);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
    }

    [Fact]
    public async Task A_run_that_is_neither_manual_nor_a_raid_is_still_skipped_inside_the_global_cooldown()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Automated(), Step(ManualId));

        result.Output.Should().Be("skipped (global cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_shoutout_step_marked_priority_inside_the_global_cooldown_waits_and_runs_before_a_manual_one()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        ActionResult result = await rig.Action.ExecuteAsync(
            Automated(),
            PriorityStep(RaiderId, priority: true)
        );

        result.Output.Should().Be("queued (global cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(RaiderId);
        rig.Queue.Peek(Channel)!.IsRaid.Should().BeTrue();

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await AssertAnnouncedAsync(rig, RaiderId);
        await rig
            .Chat.DidNotReceive()
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_shoutout_step_with_priority_off_is_still_skipped_inside_the_global_cooldown()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(
            Automated(),
            PriorityStep(RaiderId, priority: false)
        );

        result.Output.Should().Be("skipped (global cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_raid_queued_after_a_manual_shoutout_runs_first_and_the_manual_one_two_minutes_later()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, RaiderId, Arg.Any<CancellationToken>());
        await rig
            .Chat.DidNotReceive()
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(RaiderId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(ManualId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        });
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task The_same_target_queued_twice_runs_once()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult first = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        ActionResult second = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        first.Output.Should().Be("queued (global cooldown)");
        second.Output.Should().Be("already queued (global cooldown)");

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
    }

    [Fact]
    public async Task A_cleared_queue_never_sends_what_waited_in_it()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));

        rig.Queue.Clear(Channel);
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await AssertNothingSentAsync(rig);
        rig.Queue.ChannelsWithPending().Should().BeEmpty();
    }

    [Fact]
    public async Task A_queued_run_stamps_both_cooldowns_with_the_time_it_ran()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        DateTimeOffset ranAt = rig.Clock.GetUtcNow();
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        rig.ChannelCtx.LastGlobalShoutout.Should().Be(ranAt);
        rig.ChannelCtx.LastShoutoutPerUser[ManualId].Should().Be(ranAt);
    }

    [Fact]
    public async Task A_failed_native_call_on_a_queued_run_still_announces_and_the_item_is_neither_lost_nor_doubled()
    {
        Rig rig = await BuildAsync();
        rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("429 too many requests"));
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_queued_run_whose_announcement_fails_goes_back_once_and_is_then_dropped()
    {
        Rig rig = await BuildAsync();
        rig.Chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("missing scope"));
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Queue.Peek(Channel)!.Attempts.Should().Be(1);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Queue.Peek(Channel).Should().BeNull();

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig
            .Chat.Received(2)
            .SendAnnouncementAsync(
                Channel,
                Line(ManualId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
