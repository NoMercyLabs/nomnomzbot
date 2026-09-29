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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Infrastructure.Dashboard.Replay;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Rewards.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Dashboard;

/// <summary>
/// A dashboard replay re-performs a past event the way viewers saw it — the bot's chat line through the normal
/// send path, the TTS a response pipeline queues, the captured overlay alerts — and nothing else. The gift-bomb
/// shape is the real 2026-09-29 case: QTkittE gifted 5 subs, and a second gifter (Minecraft) topped the bomb up
/// by 1 under the SAME community gift id; that sub must stay out of QTkittE's chain.
/// </summary>
public sealed class ActivityReplayServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("019f146e-8303-71ef-b698-18d1098d7d7e");
    private const string CommunityGiftId = "10748597705255483136";
    private const string QtKitteId = "76424740";
    private const string MinecraftId = "112568845";
    private static readonly DateTime BombAt = new(2026, 9, 29, 16, 23, 33, DateTimeKind.Utc);

    private sealed record Harness(
        AuthDbContext Db,
        ActivityReplayService Service,
        IChatProvider Chat,
        IPipelineEngine Pipeline,
        IRenderedAlertReplayer Captures
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = ChannelId,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "39863651",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
            }
        );
        db.SaveChanges();

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();
        EventResponseExecutor executor = new(
            db,
            pipeline,
            RealTemplates(),
            chat,
            Substitute.For<IEventResponseOverlayNotifier>(),
            NullLogger<EventResponseExecutor>.Instance
        );

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddSingleton<IEventResponseExecutor>(executor)
            .BuildServiceProvider();
        IServiceScopeFactory scopes = provider.GetRequiredService<IServiceScopeFactory>();

        List<IEventResponsePresenter> presenters =
        [
            new GiftSubscriptionEventHandler(
                scopes,
                pipeline,
                NullLogger<GiftSubscriptionEventHandler>.Instance
            ),
            new GiftSubscriptionReceivedEventHandler(
                scopes,
                pipeline,
                NullLogger<GiftSubscriptionReceivedEventHandler>.Instance
            ),
            new NewSubscriptionEventHandler(
                scopes,
                pipeline,
                NullLogger<NewSubscriptionEventHandler>.Instance
            ),
            new WatchStreakHandler(
                scopes,
                pipeline,
                TimeProvider.System,
                NullLogger<WatchStreakHandler>.Instance
            ),
        ];

        JournaledDomainEventReader reader = new(
            new EventPayloadProtector(Substitute.For<ISubjectKeyService>()),
            new EventUpcasterRegistry([]),
            new DomainEventTypeRegistry()
        );
        IRenderedAlertReplayer captures = Substitute.For<IRenderedAlertReplayer>();

        ActivityReplayService service = new(
            db,
            reader,
            new GiftBombChainResolver(db, reader),
            presenters,
            captures
        );
        return new(db, service, chat, pipeline, captures);
    }

    // The real resolver's flat substitution, without its DI-backed helper pass.
    private static ITemplateResolver RealTemplates()
    {
        TemplateResolver real = new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                real.Resolve(ci.ArgAt<string>(0), ci.ArgAt<IDictionary<string, string>>(1))
            );
        return templates;
    }

    private static long _position;

    private static void Journal(AuthDbContext db, DomainEventBase domainEvent, DateTime at) =>
        db.EventJournals.Add(
            new EventJournal
            {
                EventId = domainEvent.EventId,
                BroadcasterId = ChannelId,
                StreamPosition = Interlocked.Increment(ref _position),
                EventType = domainEvent.GetType().Name,
                EventVersion = 1,
                Source = "domain",
                Payload = JsonConvert.SerializeObject(domainEvent),
                Metadata = "{}",
                OccurredAt = at,
                RecordedAt = at,
            }
        );

    private static void Respond(AuthDbContext db, string eventType, string message) =>
        db.EventResponses.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = ChannelId,
                EventType = eventType,
                ResponseType = "chat_message",
                Message = message,
                IsEnabled = true,
            }
        );

    private static GiftSubscriptionEvent Bomb(string gifterId, string gifterName, int count) =>
        new()
        {
            BroadcasterId = ChannelId,
            GifterUserId = gifterId,
            GifterDisplayName = gifterName,
            Tier = "1000",
            GiftCount = count,
            IsAnonymous = false,
            Recipients = [],
        };

    private static GiftSubscriptionReceivedEvent Recipient(
        string name,
        string gifterId,
        string gifterName
    ) =>
        new()
        {
            BroadcasterId = ChannelId,
            RecipientUserId = "id-" + name,
            RecipientDisplayName = name,
            GifterUserId = gifterId,
            GifterDisplayName = gifterName,
            IsAnonymous = false,
            Tier = "1000",
            CommunityGiftId = CommunityGiftId,
        };

    /// <summary>Seeds the real journal order: both bombs, then the six sub_gift notices (QTkittE's five and
    /// Minecraft's one), then the recipient-side channel.subscribe rows. Returns QTkittE's bomb.</summary>
    private static (
        GiftSubscriptionEvent Bomb,
        List<GiftSubscriptionReceivedEvent> Recipients
    ) SeedGiftBomb(AuthDbContext db)
    {
        GiftSubscriptionEvent qtBomb = Bomb(QtKitteId, "QTkittE", 5);
        Journal(db, qtBomb, BombAt);
        List<GiftSubscriptionReceivedEvent> qtRecipients =
        [
            Recipient("FuleSnabel", QtKitteId, "QTkittE"),
            Recipient("Labyricorn", QtKitteId, "QTkittE"),
            Recipient("atixwasfound", QtKitteId, "QTkittE"),
            Recipient("moar_power", QtKitteId, "QTkittE"),
            Recipient("MoehDev", QtKitteId, "QTkittE"),
        ];
        // Live, Minecraft's notice landed last; Twitch does not promise that order, so it is placed mid-batch
        // here — the gift-count cap alone must not be what keeps it out.
        for (int i = 0; i < qtRecipients.Count; i++)
        {
            Journal(db, qtRecipients[i], BombAt.AddMilliseconds(600 + (i * 30)));
            if (i == 1)
                Journal(db, Recipient("quuuuuuux", MinecraftId, "Minecraft"), BombAt.AddSeconds(1));
        }
        Journal(db, Bomb(MinecraftId, "Minecraft", 1), BombAt.AddSeconds(2));
        foreach (GiftSubscriptionReceivedEvent r in qtRecipients)
            Journal(
                db,
                new NewSubscriptionEvent
                {
                    BroadcasterId = ChannelId,
                    UserId = r.RecipientUserId,
                    UserDisplayName = r.RecipientDisplayName,
                    Tier = "1000",
                    IsGift = true,
                },
                BombAt.AddSeconds(3)
            );

        Respond(db, "channel.subscription.gift", "{user} gifted {count} subs to the community!");
        Respond(db, "channel.subscription.gift.received", "{user} was gifted a sub by {gifter}!");
        Respond(db, "channel.subscribe", "{user} just subscribed!");
        db.SaveChanges();
        return (qtBomb, qtRecipients);
    }

    private static List<string> SentChatLines(IChatProvider chat) =>
        [
            .. chat.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IChatProvider.SendMessageAsync))
                .Select(c => (string)c.GetArguments()[1]!),
        ];

    [Fact]
    public async Task Replaying_a_gift_bomb_replays_the_gifter_line_then_each_of_its_own_recipients_in_order()
    {
        Harness h = Build();
        (GiftSubscriptionEvent bomb, List<GiftSubscriptionReceivedEvent> recipients) = SeedGiftBomb(
            h.Db
        );

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(
            ChannelId,
            bomb.EventId.ToString()
        );

        result.IsSuccess.Should().BeTrue();
        SentChatLines(h.Chat)
            .Should()
            .Equal(
                "QTkittE gifted 5 subs to the community!",
                "FuleSnabel was gifted a sub by QTkittE!",
                "Labyricorn was gifted a sub by QTkittE!",
                "atixwasfound was gifted a sub by QTkittE!",
                "moar_power was gifted a sub by QTkittE!",
                "MoehDev was gifted a sub by QTkittE!"
            );
        result.Value.EventsReplayed.Should().Be(6);
        result.Value.ChatMessagesSent.Should().Be(6);

        // Every step's captured overlay alerts go out too, in the same order, keyed by that step's own id —
        // and the foreign gifter's recipient is never among them.
        List<string> capturedIds =
        [
            .. h.Captures.ReceivedCalls().Select(c => (string)c.GetArguments()[1]!),
        ];
        capturedIds
            .Should()
            .Equal([bomb.EventId.ToString(), .. recipients.Select(r => r.EventId.ToString())]);
    }

    [Fact]
    public async Task A_second_gifters_sub_under_the_same_community_gift_id_stays_out_of_the_chain()
    {
        Harness h = Build();
        (GiftSubscriptionEvent bomb, _) = SeedGiftBomb(h.Db);

        await h.Service.ReplayAsync(ChannelId, bomb.EventId.ToString());

        SentChatLines(h.Chat).Should().NotContain(line => line.Contains("quuuuuuux"));
    }

    [Fact]
    public async Task Replaying_the_foreign_gifters_own_bomb_replays_only_that_gifters_recipient()
    {
        Harness h = Build();
        SeedGiftBomb(h.Db);
        string minecraftBombId = h
            .Db.EventJournals.AsEnumerable()
            .Single(e =>
                e.EventType == nameof(GiftSubscriptionEvent) && e.Payload.Contains(MinecraftId)
            )
            .EventId.ToString();

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(
            ChannelId,
            minecraftBombId
        );

        SentChatLines(h.Chat)
            .Should()
            .Equal(
                "Minecraft gifted 1 subs to the community!",
                "quuuuuuux was gifted a sub by Minecraft!"
            );
        result.Value.EventsReplayed.Should().Be(2);
    }

    [Fact]
    public async Task A_replay_writes_no_activity_journal_ledger_or_handler_state_row()
    {
        Harness h = Build();
        (GiftSubscriptionEvent bomb, _) = SeedGiftBomb(h.Db);
        WatchStreakReceivedEvent streak = new()
        {
            BroadcasterId = ChannelId,
            UserId = "852857507",
            UserLogin = "moehdev",
            UserDisplayName = "MoehDev",
            StreakMonths = 5,
            ChannelPointsEarned = 450,
        };
        Journal(h.Db, streak, BombAt.AddMinutes(10));
        Respond(
            h.Db,
            "engagement.watch_streak",
            "{user.name} is on a {streak.months} stream streak!"
        );
        await h.Db.SaveChangesAsync();
        int journalRows = await h.Db.EventJournals.CountAsync();

        await h.Service.ReplayAsync(ChannelId, bomb.EventId.ToString());
        Result<ActivityReplayResult> streakReplay = await h.Service.ReplayAsync(
            ChannelId,
            streak.EventId.ToString()
        );

        // The streak's chat line WAS replayed — so the silence below is the replay path, not a dead test.
        streakReplay.Value.ChatMessagesSent.Should().Be(1);
        SentChatLines(h.Chat).Should().Contain("MoehDev is on a 5 stream streak!");

        // The live handlers (the streak's included — it also upserts the streak) log an activity row first; a
        // replay goes through the presentation half only, so no activity row appears.
        (await h.Db.ChannelEvents.CountAsync())
            .Should()
            .Be(0);
        (await h.Db.EventJournals.CountAsync()).Should().Be(journalRows);
        (await h.Db.CurrencyLedgerEntries.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_pipeline_response_replays_its_tts_without_waking_runs_parked_on_the_event()
    {
        Harness h = Build();
        Guid pipelineId = Guid.NewGuid();
        h.Db.Pipelines.Add(
            new()
            {
                Id = pipelineId,
                BroadcasterId = ChannelId,
                Name = "gift bomb tts",
                IsEnabled = true,
                GraphJsonCache = "{}",
            }
        );
        (GiftSubscriptionEvent bomb, _) = SeedGiftBomb(h.Db);
        SwitchGiftResponseToPipeline(h.Db, pipelineId);
        await h.Db.SaveChangesAsync();
        h.Pipeline.ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                    StepLogs =
                    [
                        new()
                        {
                            StepIndex = 0,
                            ActionType = "play_tts",
                            Succeeded = true,
                            Duration = TimeSpan.Zero,
                        },
                    ],
                }
            );

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(
            ChannelId,
            bomb.EventId.ToString()
        );

        result.Value.TtsQueued.Should().Be(1);
        await h
            .Pipeline.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.PipelineId == pipelineId && r.InitialVariables["count"] == "5"
                ),
                Arg.Any<CancellationToken>()
            );
        // Waking a wait_for_event run is live-event behaviour: the event is not happening again.
        await h
            .Pipeline.DidNotReceiveWithAnyArgs()
            .ResumeSuspendedRunsForEventAsync(default, default!, default!);
        // The pipeline queued its own TTS, so a captured utterance must not be re-sent on top of it.
        await h
            .Captures.Received(1)
            .ResendAsync(ChannelId, bomb.EventId.ToString(), false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_event_with_no_journal_row_falls_back_to_its_captured_alerts_including_tts()
    {
        Harness h = Build();
        string legacyId = Guid.NewGuid().ToString();
        h.Captures.ResendAsync(ChannelId, legacyId, true, Arg.Any<CancellationToken>()).Returns(2);

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(ChannelId, legacyId);

        result.IsSuccess.Should().BeTrue();
        result.Value.WidgetsNotified.Should().Be(2);
        result.Value.ChatMessagesSent.Should().Be(0);
        SentChatLines(h.Chat).Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_with_nothing_stored_and_nothing_captured_is_not_found()
    {
        Harness h = Build();

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(
            ChannelId,
            Guid.NewGuid().ToString()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task A_gifted_recipients_own_subscribe_row_stays_silent_on_replay_as_it_did_live()
    {
        Harness h = Build();
        SeedGiftBomb(h.Db);
        string giftedSubId = h
            .Db.EventJournals.AsEnumerable()
            .First(e => e.EventType == nameof(NewSubscriptionEvent))
            .EventId.ToString();

        Result<ActivityReplayResult> result = await h.Service.ReplayAsync(ChannelId, giftedSubId);

        result.IsSuccess.Should().BeTrue();
        SentChatLines(h.Chat).Should().BeEmpty();
    }

    private static void SwitchGiftResponseToPipeline(AuthDbContext db, Guid pipelineId)
    {
        NomNomzBot.Domain.Commands.Entities.EventResponse row = db.EventResponses.Local.Single(r =>
            r.EventType == "channel.subscription.gift"
        );
        row.ResponseType = "pipeline";
        row.Message = null;
        row.PipelineId = pipelineId;
    }
}
