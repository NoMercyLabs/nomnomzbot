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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.Kick;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick chat READ translation: a verified <c>chat.message.sent</c> body resolves the
/// kick-provider tenant and publishes the canonical <see cref="ChatMessageReceivedEvent"/> with the
/// exact identity, role flags (from Kick badge types), and provider key the one-substrate consumers
/// expect; an unknown broadcaster is skipped; a redelivered message id (already persisted) publishes
/// nothing; a malformed body is swallowed, never thrown into the webhook path. Also proves the
/// <c>livestream.status.updated</c> live tracker: go-live/end stamps the tenant's <c>IsLive</c> (+ title)
/// that the dashboard's <c>platformsLive</c> aggregates.
/// </summary>
public sealed class KickWebhookIngestTests
{
    private static readonly Guid Tenant = Guid.Parse("0192d000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("0192d000-0000-7000-8000-0000000000a9");

    private const string ChatBody = """
        {
          "message_id": "kick-msg-1",
          "broadcaster": { "user_id": 12345, "username": "StreamerGal", "channel_slug": "streamergal" },
          "sender": {
            "user_id": 678,
            "username": "ChatterBoi",
            "channel_slug": "chatterboi",
            "identity": { "username_color": "#FF0000", "badges": [ { "text": "Moderator", "type": "moderator" }, { "text": "Sub", "type": "subscriber", "count": 3 } ] }
          },
          "content": "hello kick [emote:37226:EZ]",
          "emotes": [ { "emote_id": "37226", "positions": [ { "s": 11, "e": 26 } ] } ],
          "created_at": "2026-07-11T12:34:56Z"
        }
        """;

    private static (KickWebhookIngest Ingest, AuthDbContext Db, IEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = "12345",
                Name = "streamergal",
                NameNormalized = "streamergal",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        IEventBus bus = Substitute.For<IEventBus>();
        KickWebhookIngest ingest = new(
            db,
            bus,
            TimeProvider.System,
            NullLogger<KickWebhookIngest>.Instance
        );
        return (ingest, db, bus);
    }

    [Fact]
    public async Task A_chat_message_publishes_the_canonical_event_with_kick_identity_and_roles()
    {
        (KickWebhookIngest ingest, _, IEventBus bus) = Build();
        ChatMessageReceivedEvent? published = null;
        bus.When(b =>
                b.PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            )
            .Do(call => published = call.Arg<ChatMessageReceivedEvent>());

        await ingest.HandleChatMessageAsync(ChatBody);

        await bus.Received(1)
            .PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
        published.Should().NotBeNull();
        published!.BroadcasterId.Should().Be(Tenant);
        published.Provider.Should().Be(AuthEnums.Platform.Kick);
        published.MessageId.Should().Be("kick-msg-1");
        published.UserId.Should().Be("678");
        published.UserDisplayName.Should().Be("ChatterBoi");
        published.UserLogin.Should().Be("chatterboi", "the channel slug is the stable handle");
        published.Message.Should().Be("hello kick [emote:37226:EZ]");
        published.OccurredAt.Should().Be(DateTimeOffset.Parse("2026-07-11T12:34:56Z"));
        published.IsModerator.Should().BeTrue("the moderator badge is present");
        published.IsSubscriber.Should().BeTrue("the subscriber badge is present");
        published.IsBroadcaster.Should().BeFalse();
        published.IsVip.Should().BeFalse();
    }

    [Fact]
    public async Task The_broadcasters_own_message_flags_is_broadcaster_by_identity_match()
    {
        (KickWebhookIngest ingest, _, IEventBus bus) = Build();
        ChatMessageReceivedEvent? published = null;
        bus.When(b =>
                b.PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>())
            )
            .Do(call => published = call.Arg<ChatMessageReceivedEvent>());
        string body = ChatBody.Replace("\"user_id\": 678", "\"user_id\": 12345");

        await ingest.HandleChatMessageAsync(body);

        published!.IsBroadcaster.Should().BeTrue("sender.user_id equals broadcaster.user_id");
    }

    [Fact]
    public async Task An_unknown_broadcaster_is_skipped()
    {
        (KickWebhookIngest ingest, _, IEventBus bus) = Build();
        string body = ChatBody.Replace("12345", "99999");

        await ingest.HandleChatMessageAsync(body);

        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_redelivered_message_id_publishes_nothing()
    {
        (KickWebhookIngest ingest, AuthDbContext db, IEventBus bus) = Build();
        db.ChatMessages.Add(
            new ChatMessage
            {
                Id = "kick-msg-1",
                BroadcasterId = Tenant,
                UserId = "678",
                Username = "chatterboi",
                DisplayName = "ChatterBoi",
                UserType = "viewer",
                Message = "hello kick [emote:37226:EZ]",
            }
        );
        db.SaveChanges();

        await ingest.HandleChatMessageAsync(ChatBody);

        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_malformed_body_is_swallowed_not_thrown()
    {
        (KickWebhookIngest ingest, _, IEventBus bus) = Build();

        await ingest.HandleChatMessageAsync("not json at all");
        await ingest.HandleChatMessageAsync("""{"message_id":"x"}""");

        await bus.DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<ChatMessageReceivedEvent>(), Arg.Any<CancellationToken>());
    }

    // ─── livestream.status.updated — Kick's live tracker behind platformsLive ───

    private const string LiveBody = """
        {
          "broadcaster": { "user_id": 12345, "username": "StreamerGal", "channel_slug": "streamergal" },
          "is_live": true,
          "title": "Bird up!",
          "started_at": "2026-07-16T09:00:00Z",
          "ended_at": null
        }
        """;

    [Fact]
    public async Task A_livestream_going_live_stamps_the_tenant_live_with_its_title()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();

        await ingest.HandleLivestreamStatusAsync(LiveBody);

        Channel tenant = db.Channels.Single(c => c.Id == Tenant);
        tenant.IsLive.Should().BeTrue();
        tenant.Title.Should().Be("Bird up!");
    }

    [Fact]
    public async Task A_livestream_ending_clears_the_tenant_live_flag()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();
        db.Channels.Single(c => c.Id == Tenant).IsLive = true;
        db.SaveChanges();
        string body = LiveBody
            .Replace("\"is_live\": true", "\"is_live\": false")
            .Replace("\"ended_at\": null", "\"ended_at\": \"2026-07-16T11:00:00Z\"");

        await ingest.HandleLivestreamStatusAsync(body);

        db.Channels.Single(c => c.Id == Tenant).IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task A_livestream_status_for_an_unknown_broadcaster_is_skipped()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();
        string body = LiveBody.Replace("12345", "99999");

        await ingest.HandleLivestreamStatusAsync(body);

        db.Channels.Single(c => c.Id == Tenant).IsLive.Should().BeFalse();
    }

    [Fact]
    public async Task A_malformed_livestream_body_is_swallowed_not_thrown()
    {
        (KickWebhookIngest ingest, AuthDbContext db, _) = Build();

        await ingest.HandleLivestreamStatusAsync("not json at all");
        await ingest.HandleLivestreamStatusAsync("""{"title":"no identity"}""");

        db.Channels.Single(c => c.Id == Tenant).IsLive.Should().BeFalse();
    }
}
