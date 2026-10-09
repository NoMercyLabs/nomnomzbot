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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Security;
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
    private static readonly Guid Moderator = Guid.Parse("0192a000-0000-7000-8000-00000000b3a1");
    private const string ManualId = "111001";
    private const string RaiderId = "222002";

    private sealed class Rig
    {
        public required ShoutoutAction Action { get; init; }
        public required ShoutoutQueueWorker Worker { get; init; }
        public required ShoutoutQueue Queue { get; init; }
        public required ITwitchChatApi Chat { get; init; }
        public required IChatProvider BotChat { get; init; }
        public required List<OutboundSanction?> SeenSanctions { get; init; }
        public required ITtsDispatchService Tts { get; init; }
        public required ChannelContext ChannelCtx { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        public required AuthDbContext Db { get; init; }
        public required IChannelRegistry Registry { get; init; }
        public required IOutboundSanctionAccessor Sanctions { get; init; }
        public IServiceScopeFactory? ScopeFactory { get; init; }
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

    // Steps pin this template so the queue tests assert one exact line, not a random pick from the tone pool.
    private const string Template = "Go check out {target.name}!";

    private static string Line(string id) => $"Go check out Name{id}!";

    private static async Task<Rig> BuildAsync(bool durable = false)
    {
        FakeTimeProvider clock = new(new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

        OutboundSanctionAccessor sanctions = new();
        List<OutboundSanction?> seenSanctions = [];
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        // Only the queued native call runs under a sanction; the announcement went out earlier, in the chat run.
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                seenSanctions.Add(sanctions.Current);
                return Result.Success();
            });
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        IChatProvider botChat = Substitute.For<IChatProvider>();
        botChat
            .SendMessageAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

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
        db.Users.Add(
            new()
            {
                Id = Moderator,
                TwitchUserId = "viewer-1",
                Username = "viewer",
                UsernameNormalized = "viewer",
                DisplayName = "viewer",
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

        IServiceScopeFactory? scopeFactory = null;
        if (durable)
        {
            ServiceCollection services = new();
            services.AddSingleton<IApplicationDbContext>(db);
            scopeFactory = services
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();
        }
        ShoutoutQueue queue = new(scopeFactory);
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
                queue,
                botChat: botChat
            ),
            Worker = ShoutoutTestFactory.Worker(queue, chat, registry, db, tts, clock, sanctions),
            BotChat = botChat,
            SeenSanctions = seenSanctions,
            Queue = queue,
            Chat = chat,
            Tts = tts,
            ChannelCtx = channelCtx,
            Clock = clock,
            Db = db,
            Registry = registry,
            Sanctions = sanctions,
            ScopeFactory = scopeFactory,
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
                ["template"] = JsonSerializer.SerializeToElement(Template),
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
                ["template"] = JsonSerializer.SerializeToElement(Template),
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

    // Nothing goes out while the native call is waiting.
    private static async Task AssertAnnouncedNowNativeWaitingAsync(Rig rig)
    {
        await AssertNothingSentAsync(rig);
    }

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
    public async Task A_restart_recovers_the_waiting_message_and_delivers_it_once()
    {
        Rig rig = await BuildAsync(durable: true);
        StampGlobalCooldown(rig);

        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        await AssertNothingSentAsync(rig);
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(1);
        ShoutoutQueue restartedQueue = new(rig.ScopeFactory);
        ShoutoutQueueWorker restartedWorker = ShoutoutTestFactory.Worker(
            restartedQueue,
            rig.Chat,
            rig.Registry,
            rig.Db,
            rig.Tts,
            rig.Clock,
            rig.Sanctions
        );

        rig.Clock.Advance(GlobalCooldown);
        await restartedWorker.ProcessDueAsync(CancellationToken.None);
        await restartedWorker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(0);
        restartedQueue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task Dashboard_shoutout_waits_for_the_rate_limit_then_sends_only_the_native_call()
    {
        Rig rig = await BuildAsync(durable: true);
        DateTimeOffset now = rig.Clock.GetUtcNow();
        await rig.Queue.EnqueueAsync(
            new QueuedShoutout(
                Channel,
                User(ManualId),
                string.Empty,
                false,
                GlobalCooldown,
                TimeSpan.FromMinutes(60),
                now,
                ActorUserId: Moderator,
                NotBefore: now.Add(GlobalCooldown)
            ),
            CancellationToken.None
        );

        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        rig.Db.ChannelEvents.Single().Type.Should().Be("bot.shoutout.pending");

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await rig.Chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default);
        rig.Db.ChannelEvents.Single().Type.Should().Be("bot.shoutout.sent");
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_restart_preserves_order_for_items_queued_at_the_same_clock_time()
    {
        Rig rig = await BuildAsync(durable: true);
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));
        await rig.Action.ExecuteAsync(Raid(), Step(ManualId));
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(2);

        ShoutoutQueue restartedQueue = new(rig.ScopeFactory);
        ShoutoutQueueWorker restartedWorker = ShoutoutTestFactory.Worker(
            restartedQueue,
            rig.Chat,
            rig.Registry,
            rig.Db,
            rig.Tts,
            rig.Clock,
            rig.Sanctions
        );
        rig.Clock.Advance(GlobalCooldown);
        await restartedWorker.ProcessDueAsync(CancellationToken.None);
        rig.Clock.Advance(GlobalCooldown);
        await restartedWorker.ProcessDueAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            rig.Chat.SendShoutoutAsync(Channel, RaiderId, Arg.Any<CancellationToken>());
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(RaiderId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
            rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(ManualId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        });
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(0);
    }

    [Fact]
    public async Task A_restart_after_recorded_delivery_clears_the_pending_item_without_sending_twice()
    {
        Rig rig = await BuildAsync(durable: true);
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        rig.Clock.Advance(GlobalCooldown);

        ShoutoutSender sender = ShoutoutTestFactory.Sender(
            rig.Chat,
            rig.Registry,
            rig.Db,
            rig.Tts,
            rig.Clock
        );
        await sender.SendAsync(
            new(Channel, User(ManualId), Line(ManualId), true),
            CancellationToken.None
        );
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.sent").Should().Be(1);
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(1);

        ShoutoutQueue restartedQueue = new(rig.ScopeFactory);
        ShoutoutQueueWorker restartedWorker = ShoutoutTestFactory.Worker(
            restartedQueue,
            rig.Chat,
            rig.Registry,
            rig.Db,
            rig.Tts,
            rig.Clock,
            rig.Sanctions
        );
        rig.Clock.Advance(TimeSpan.FromMinutes(60));
        await restartedWorker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(0);
    }

    [Fact]
    public async Task A_native_only_dashboard_shoutout_does_not_discard_a_queued_announcement()
    {
        Rig rig = await BuildAsync(durable: true);
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        rig.Db.ChannelEvents.Add(
            new NomNomzBot.Domain.Identity.Entities.ChannelEvent
            {
                Id = Guid.CreateVersion7().ToString(),
                ChannelId = Channel,
                Type = "bot.shoutout.sent",
                Data = JsonSerializer.Serialize(new { toUserId = ManualId }),
                CreatedAt = rig.Clock.GetUtcNow().UtcDateTime.AddSeconds(1),
            }
        );
        await rig.Db.SaveChangesAsync();

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(0);
    }

    [Fact]
    public async Task A_queued_shoutout_waits_to_announce_and_speak_until_native_delivery()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(ManualId);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_queued_chat_shoutout_tells_chat_once_that_it_waits()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        await rig
            .BotChat.Received(1)
            .SendMessageAsync(
                Channel,
                Arg.Is<string>(text => text.Contains("Name" + ManualId)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_queued_raid_shoutout_does_not_talk_in_chat()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));

        await rig.BotChat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }

    [Fact]
    public async Task A_raid_inside_the_global_cooldown_waits_then_runs_in_full_when_the_cooldown_passes()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Raid(), Step(RaiderId, tts: true));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("queued (cooldown)");
        await AssertAnnouncedNowNativeWaitingAsync(rig);
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

        await AssertAnnouncedNowNativeWaitingAsync(rig);
        rig.Queue.Peek(Channel).Should().NotBeNull();
    }

    [Fact]
    public async Task A_manual_shoutout_inside_the_global_cooldown_waits_then_runs_in_full()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Output.Should().Be("queued (cooldown)");
        await AssertAnnouncedNowNativeWaitingAsync(rig);

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
    }

    [Fact]
    public async Task A_manual_shoutout_inside_the_per_user_cooldown_announces_and_speaks_without_native_delivery()
    {
        Rig rig = await BuildAsync();
        rig.ChannelCtx.LastShoutoutPerUser[ManualId] = rig.Clock.GetUtcNow();

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("announced (per-user cooldown)");
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        rig.Queue.Peek(Channel).Should().BeNull();
        rig.ChannelCtx.LastGlobalShoutout.Should().BeNull();
        rig.Db.ChannelEvents.Should()
            .ContainSingle(e =>
                e.Type == "bot.shoutout.sent"
                && e.Data != null
                && e.Data.Contains("\"toUserId\":\"111001\"")
                && e.Data.Contains("\"source\":\"announce-only\"")
            );
        ActionResult duplicate = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        duplicate.Output.Should().Be("already announced (per-user cooldown)");
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Clock.Advance(TimeSpan.FromMinutes(60));
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task A_manual_shoutout_inside_both_cooldowns_announces_without_queueing_a_native_call()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        rig.ChannelCtx.LastShoutoutPerUser[ManualId] = rig.Clock.GetUtcNow();

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        result.Output.Should().Be("announced (per-user cooldown)");
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        rig.Clock.Advance(TimeSpan.FromMinutes(58));
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
    }

    [Fact]
    public async Task An_event_run_waits_inside_the_global_cooldown()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(Automated(), Step(ManualId));

        result.Output.Should().Be("queued (cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(ManualId);
    }

    [Fact]
    public async Task A_timed_run_waits_inside_the_global_cooldown()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        PipelineExecutionContext timed = Ctx(string.Empty, "timer.tick");

        await rig.Action.ExecuteAsync(timed, Step(ManualId, tts: false));

        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Announcement.Should().Be(Line(ManualId));
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await AssertAnnouncedAsync(rig, ManualId);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    [Fact]
    public async Task An_unexpected_native_rate_limit_queues_the_announcement()
    {
        Rig rig = await BuildAsync();
        int calls = 0;
        rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>())
            .Returns(_ =>
                ++calls == 1
                    ? Result.Failure("429", TwitchErrorCodes.RateLimited)
                    : Result.Success()
            );

        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        await rig.Chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(ManualId);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
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

        result.Output.Should().Be("queued (cooldown)");
        await AssertNothingSentAsync(rig);
        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
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
    public async Task A_shoutout_step_with_priority_off_still_waits_inside_the_global_cooldown()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult result = await rig.Action.ExecuteAsync(
            Automated(),
            PriorityStep(RaiderId, priority: false)
        );

        result.Output.Should().Be("queued (cooldown)");
        await AssertNothingSentAsync(rig);
        rig.Queue.Peek(Channel)!.Target.Id.Should().Be(RaiderId);
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
            rig.Chat.SendShoutoutAsync(Channel, RaiderId, Arg.Any<CancellationToken>());
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(RaiderId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
            rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
            rig.Chat.SendAnnouncementAsync(
                Channel,
                Line(ManualId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        });
        await AssertAnnouncedAsync(rig, RaiderId);
        await AssertAnnouncedAsync(rig, ManualId);
        rig.Queue.Peek(Channel).Should().BeNull();
    }

    [Fact]
    public async Task The_same_target_queued_twice_runs_once()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        ActionResult first = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        ActionResult second = await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        first.Output.Should().Be("queued (cooldown)");
        second.Output.Should().Be("already queued (cooldown)");

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        // The duplicate ask does not duplicate the announcement.
        await rig
            .Chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                Line(ManualId),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
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

        await rig.Chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!);
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
    public async Task A_failed_native_call_on_a_queued_run_sends_no_announcement()
    {
        Rig rig = await BuildAsync();
        rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("429 too many requests", TwitchErrorCodes.RateLimited));
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        await rig
            .Chat.Received(2)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
        await rig.Chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
        rig.Queue.Peek(Channel).Should().NotBeNull();
    }

    [Fact]
    public async Task A_queued_run_whose_native_call_fails_for_good_is_dropped_without_a_retry()
    {
        Rig rig = await BuildAsync();
        rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("missing scope", TwitchErrorCodes.MissingScope));
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        rig.Queue.Peek(Channel).Should().BeNull();

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_queued_run_that_hits_a_rate_limit_waits_and_is_sent_when_the_window_opens()
    {
        Rig rig = await BuildAsync(durable: true);
        int calls = 0;
        rig.Chat.SendShoutoutAsync(Channel, ManualId, Arg.Any<CancellationToken>())
            .Returns(_ =>
                ++calls <= 3
                    ? Result.Failure("429", TwitchErrorCodes.RateLimited)
                    : Result.Success()
            );
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending").Should().Be(1);

        for (int pass = 0; pass < 4; pass++)
        {
            rig.Clock.Advance(GlobalCooldown);
            await rig.Worker.ProcessDueAsync(CancellationToken.None);
            rig.Db.ChannelEvents.Count(e => e.Type == "bot.shoutout.pending")
                .Should()
                .Be(pass == 3 ? 0 : 1);
        }

        calls.Should().Be(4);
        rig.Queue.Peek(Channel).Should().BeNull();
        await AssertAnnouncedAsync(rig, ManualId);
        await AssertSpokenForAsync(rig, ManualId);
    }

    [Fact]
    public async Task A_queued_manual_shoutout_is_sent_under_a_user_action_sanction_naming_the_requester()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        OutboundSanction? sanction = rig.SeenSanctions.Should().ContainSingle().Subject;
        sanction!.Basis.Should().Be(OutboundSanctionBasis.UserAction);
        sanction.Detail.Should().Be("moderation:shoutout");
        sanction.ActorUserId.Should().Be(Moderator);
    }

    [Fact]
    public async Task A_queued_raid_shoutout_is_sent_under_a_channel_configuration_sanction()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);
        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));

        rig.Clock.Advance(GlobalCooldown);
        await rig.Worker.ProcessDueAsync(CancellationToken.None);

        rig.SeenSanctions.Should()
            .ContainSingle()
            .Which!.Basis.Should()
            .Be(OutboundSanctionBasis.ChannelConfiguration);
    }

    [Fact]
    public async Task A_queued_shoutout_posts_no_announcement_and_one_queued_reply_for_the_chat_request()
    {
        Rig rig = await BuildAsync();
        StampGlobalCooldown(rig);

        PipelineExecutionContext manual = Manual();
        await rig.Action.ExecuteAsync(manual, Step(ManualId));
        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));
        await rig.Action.ExecuteAsync(Raid(), Step(RaiderId));

        await rig.BotChat.ReceivedWithAnyArgs(1).SendMessageAsync(default, default!);
        await AssertNothingSentAsync(rig);
        manual.RepliedToChat.Should().BeFalse();
    }

    [Fact]
    public async Task A_shoutout_sent_immediately_posts_no_queued_reply()
    {
        Rig rig = await BuildAsync();

        await rig.Action.ExecuteAsync(Manual(), Step(ManualId));

        await rig.BotChat.DidNotReceiveWithAnyArgs().SendMessageAsync(default, default!);
    }
}
