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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// Proves the YouTube chat ingest end to end at the worker seam (combined-chat item 6): a connected
/// streamer going live gets their YouTube presence provisioned as its OWN tenant <c>Channel</c> row, the
/// first page only bootstraps the paging cursor (history never floods the feed), every subsequent message
/// is published as the canonical <see cref="ChatMessageReceivedEvent"/> with <c>Provider = youtube</c> and
/// the author's role flags mapped, already-persisted messages are not re-published, a dead chat id drops
/// the channel back to cheap liveness probing, and the offline cadence honors the 2-minute probe gate.
/// </summary>
public sealed class YouTubeLiveChatPollWorkerTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199b000-0000-7000-8000-0000000000b1");
    private static readonly Guid Owner = Guid.Parse("0199b000-0000-7000-8000-0000000000a1");
    private static readonly DateTimeOffset Start = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Going_live_provisions_the_youtube_tenant_and_publishes_live_messages_after_the_bootstrap_page()
    {
        (
            YouTubeLiveChatPollWorker worker,
            ScriptedLiveChatClient client,
            RecordingEventBus bus,
            AuthDbContext db,
            FakeTimeProvider time,
            YouTubeLiveChatSessionRegistry sessions
        ) = await BuildConnectedAsync();

        client.LivenessResults.Enqueue(
            Result.Success<YouTubeActiveChat?>(new YouTubeActiveChat("b1", "chat-1", "Live!"))
        );
        // Page 1 = history bootstrap: its items must NOT reach the feed; only the cursor is kept.
        client.PageResults.Enqueue(
            Result.Success(new YouTubeLiveChatPage([Message("hist-1", "old line")], "tok-1", 1000))
        );
        // Page 2 = live traffic: the owner speaks, then a channel member.
        client.PageResults.Enqueue(
            Result.Success(
                new YouTubeLiveChatPage(
                    [
                        Message("m-1", "hello chat", isOwner: true),
                        Message("m-2", "hi!", isMember: true),
                    ],
                    "tok-2",
                    1000
                )
            )
        );

        await worker.TickAsync(CancellationToken.None); // liveness → live, tenant provisioned
        await worker.TickAsync(CancellationToken.None); // bootstrap page (cursor only)
        time.Advance(TimeSpan.FromSeconds(6));
        await worker.TickAsync(CancellationToken.None); // live page → publishes

        // The platform presence is its own tenant row keyed by the streamer's YouTube channel id.
        Channel tenant = await db
            .Channels.IgnoreQueryFilters()
            .SingleAsync(c => c.Provider == AuthEnums.Platform.YouTube);
        tenant.OwnerUserId.Should().Be(Owner);
        tenant.ExternalChannelId.Should().Be("UCstreamer");
        tenant.TwitchChannelId.Should().BeNull();

        // Going live also opens the SEND path: the session registry carries the active chat + the
        // primary channel whose token authorizes writes (slice-3 YouTubeChatPlatform reads this).
        YouTubeLiveChatSession? session = sessions.Get(tenant.Id);
        session.Should().NotBeNull();
        session!.LiveChatId.Should().Be("chat-1");
        session.PrimaryBroadcasterId.Should().Be(Broadcaster);

        // Paging: bootstrap read with no token, the live read continues from the bootstrap cursor.
        client.PageTokensSeen.Should().Equal(null, "tok-1");

        List<ChatMessageReceivedEvent> published = bus
            .Published.OfType<ChatMessageReceivedEvent>()
            .ToList();
        published.Should().HaveCount(2, "history must bootstrap silently; only live lines publish");
        published.Select(e => e.MessageId).Should().Equal("m-1", "m-2");

        ChatMessageReceivedEvent ownerLine = published[0];
        ownerLine.Provider.Should().Be(AuthEnums.Platform.YouTube);
        ownerLine.BroadcasterId.Should().Be(tenant.Id);
        ownerLine.TwitchBroadcasterId.Should().Be("UCstreamer");
        ownerLine.UserId.Should().Be("UCauthor-m-1");
        ownerLine.Message.Should().Be("hello chat");
        ownerLine.Fragments.Should().ContainSingle(f => f.Type == "text" && f.Text == "hello chat");
        ownerLine.IsBroadcaster.Should().BeTrue();
        ownerLine.OccurredAt.Should().Be(Start); // the API's publishedAt, not "now"

        published[1].IsSubscriber.Should().BeTrue("a channel member maps to the subscriber flag");
        published[1].IsBroadcaster.Should().BeFalse();
    }

    [Fact]
    public async Task A_message_already_persisted_is_not_republished()
    {
        (
            YouTubeLiveChatPollWorker worker,
            ScriptedLiveChatClient client,
            RecordingEventBus bus,
            AuthDbContext db,
            FakeTimeProvider time,
            _
        ) = await BuildConnectedAsync();

        // The overlap victim: m-1 already reached the ChatMessages table on an earlier page.
        db.ChatMessages.Add(
            new NomNomzBot.Domain.Chat.Entities.ChatMessage
            {
                Id = "m-1",
                BroadcasterId = Broadcaster,
                UserId = "UCauthor-m-1",
                Username = "someone",
                DisplayName = "Someone",
                UserType = "viewer",
                Message = "hello chat",
            }
        );
        await db.SaveChangesAsync();

        client.LivenessResults.Enqueue(
            Result.Success<YouTubeActiveChat?>(new YouTubeActiveChat("b1", "chat-1", "Live!"))
        );
        client.PageResults.Enqueue(Result.Success(new YouTubeLiveChatPage([], "tok-1", 1000)));
        client.PageResults.Enqueue(
            Result.Success(
                new YouTubeLiveChatPage(
                    [Message("m-1", "hello chat"), Message("m-3", "fresh line")],
                    "tok-2",
                    1000
                )
            )
        );

        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(6));
        await worker.TickAsync(CancellationToken.None);

        bus.Published.OfType<ChatMessageReceivedEvent>()
            .Select(e => e.MessageId)
            .Should()
            .Equal("m-3");
    }

    [Fact]
    public async Task A_dead_chat_id_drops_the_channel_back_to_liveness_probing()
    {
        (
            YouTubeLiveChatPollWorker worker,
            ScriptedLiveChatClient client,
            RecordingEventBus bus,
            AuthDbContext db,
            FakeTimeProvider time,
            YouTubeLiveChatSessionRegistry sessions
        ) = await BuildConnectedAsync();

        client.LivenessResults.Enqueue(
            Result.Success<YouTubeActiveChat?>(new YouTubeActiveChat("b1", "chat-1", "Live!"))
        );
        client.PageResults.Enqueue(
            Result.Failure<YouTubeLiveChatPage>(
                "The YouTube live chat is no longer available.",
                "NOT_FOUND"
            )
        );
        client.LivenessResults.Enqueue(Result.Success<YouTubeActiveChat?>(null));

        await worker.TickAsync(CancellationToken.None); // → live
        await worker.TickAsync(CancellationToken.None); // page read → NOT_FOUND → offline
        time.Advance(TimeSpan.FromMinutes(3));
        await worker.TickAsync(CancellationToken.None); // due again → probes liveness, not messages

        client.LivenessCalls.Should().Be(2, "the ended chat must fall back to liveness probing");
        client.PageCalls.Should().Be(1);
        bus.Published.OfType<ChatMessageReceivedEvent>().Should().BeEmpty();
        // The send path's session cleared with the chat — a send after the end must not target a dead id.
        Channel tenant = await db
            .Channels.IgnoreQueryFilters()
            .SingleAsync(c => c.Provider == AuthEnums.Platform.YouTube);
        sessions.Get(tenant.Id).Should().BeNull();
    }

    [Fact]
    public async Task An_offline_channel_is_probed_on_the_liveness_cadence_not_every_tick()
    {
        (
            YouTubeLiveChatPollWorker worker,
            ScriptedLiveChatClient client,
            _,
            _,
            FakeTimeProvider time,
            _
        ) = await BuildConnectedAsync();

        client.LivenessResults.Enqueue(Result.Success<YouTubeActiveChat?>(null));
        client.LivenessResults.Enqueue(Result.Success<YouTubeActiveChat?>(null));

        await worker.TickAsync(CancellationToken.None); // first probe: offline
        time.Advance(TimeSpan.FromSeconds(30));
        await worker.TickAsync(CancellationToken.None); // inside the 2-min gate → no call
        client.LivenessCalls.Should().Be(1, "probes are quota-billed — one per liveness window");

        time.Advance(TimeSpan.FromMinutes(2));
        await worker.TickAsync(CancellationToken.None);
        client.LivenessCalls.Should().Be(2);
    }

    [Fact]
    public async Task A_channel_without_a_usable_token_never_reaches_the_api()
    {
        (
            YouTubeLiveChatPollWorker worker,
            ScriptedLiveChatClient client,
            RecordingEventBus bus,
            _,
            _,
            _
        ) = await BuildConnectedAsync(accessToken: null);

        await worker.TickAsync(CancellationToken.None);

        client.LivenessCalls.Should().Be(0);
        client.PageCalls.Should().Be(0);
        bus.Published.Should().BeEmpty();
    }

    // ── shared scaffolding ──────────────────────────────────────────────────

    private static async Task<(
        YouTubeLiveChatPollWorker Worker,
        ScriptedLiveChatClient Client,
        RecordingEventBus Bus,
        AuthDbContext Db,
        FakeTimeProvider Time,
        YouTubeLiveChatSessionRegistry Sessions
    )> BuildConnectedAsync(string? accessToken = "bearer-token")
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                TwitchChannelId = "tw123",
                ExternalChannelId = "tw123",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.Services.Add(
            new Service
            {
                Name = "youtube",
                Enabled = true,
                BroadcasterId = Broadcaster,
                AccessToken = "sealed-envelope",
            }
        );
        await db.SaveChangesAsync();

        ScriptedLiveChatClient client = new();
        RecordingEventBus bus = new();
        FakeTimeProvider time = new(Start);
        YouTubeLiveChatSessionRegistry sessionRegistry = new();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<IYouTubeAccessTokenProvider>(new FixedTokenProvider(accessToken));
        services.AddScoped<IPlatformChannelProvisioner, PlatformChannelProvisioner>();
        services.AddSingleton<IEventBus>(bus);
        ServiceProvider provider = services.BuildServiceProvider();

        YouTubeLiveChatPollWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            client,
            sessionRegistry,
            time,
            NullLogger<YouTubeLiveChatPollWorker>.Instance
        );

        return (worker, client, bus, db, time, sessionRegistry);
    }

    private static YouTubeLiveChatMessage Message(
        string id,
        string text,
        bool isOwner = false,
        bool isMember = false
    ) =>
        new(
            id,
            $"UCauthor-{id}",
            $"Author {id}",
            text,
            Start,
            IsModerator: false,
            IsOwner: isOwner,
            IsMember: isMember
        );

    /// <summary>Scripted transport: queued liveness/page results plus call + cursor recording, so each
    /// test proves exactly which API surface was hit and with which paging token.</summary>
    private sealed class ScriptedLiveChatClient : IYouTubeLiveChatClient
    {
        public Queue<Result<YouTubeActiveChat?>> LivenessResults { get; } = new();
        public Queue<Result<YouTubeLiveChatPage>> PageResults { get; } = new();
        public int LivenessCalls { get; private set; }
        public int PageCalls { get; private set; }
        public List<string?> PageTokensSeen { get; } = [];

        public Task<Result<YouTubeActiveChat?>> GetActiveLiveChatAsync(
            string accessToken,
            CancellationToken cancellationToken = default
        )
        {
            LivenessCalls++;
            return Task.FromResult(
                LivenessResults.Count > 0
                    ? LivenessResults.Dequeue()
                    : Result.Success<YouTubeActiveChat?>(null)
            );
        }

        public Task<Result<YouTubeLiveChatPage>> ListMessagesAsync(
            string accessToken,
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken = default
        )
        {
            PageCalls++;
            PageTokensSeen.Add(pageToken);
            return Task.FromResult(PageResults.Dequeue());
        }

        public Task<Result<YouTubeOwnChannel>> GetOwnChannelAsync(
            string accessToken,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new YouTubeOwnChannel("UCstreamer", "Streamer YT")));

        public Task<Result> SendMessageAsync(
            string accessToken,
            string liveChatId,
            string text,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success());
    }

    private sealed class FixedTokenProvider(string? token) : IYouTubeAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(token);
    }
}
