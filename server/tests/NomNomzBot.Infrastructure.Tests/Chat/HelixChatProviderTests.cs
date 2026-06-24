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
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the Helix chat-send picks the right SENDER identity (the <c>sender_id</c> on the Helix
/// <c>POST /helix/chat/messages</c> body). The sender must be the same account whose token signs the send
/// (the bot resolution order, onboarding.md two-account model): a registered bot account when present, else —
/// on a self-host install with no bot account — the streamer's OWN main account. This is the second half of
/// the main-account-is-the-bot fix: a resolved token with no matching sender_id would still 400 at Twitch.
/// </summary>
public sealed class HelixChatProviderTests
{
    private static readonly Guid Owner = Guid.Parse("0192a000-0000-7000-8000-00000000ab01");
    private const string OwnerTwitchChannelId = "owner-channel-77";
    private const string OwnerTwitchUserId = "owner-user-77";
    private const string BotTwitchUserId = "bot-user-99";

    private static (HelixChatProvider Provider, CapturingHelixTransport Transport) Build(
        AuthDbContext db
    )
    {
        CapturingHelixTransport transport = new();
        HelixChatProvider provider = new(
            transport,
            Substitute.For<ITwitchModerationApi>(),
            new StubIdentityResolver(Owner, OwnerTwitchChannelId),
            db,
            Options.Create(
                new TwitchOptions
                {
                    ClientId = "c",
                    ClientSecret = "s",
                    BotUsername = "b",
                }
            ),
            NullLogger<HelixChatProvider>.Instance
        );
        return (provider, transport);
    }

    private static async Task AddConnectionAsync(
        AuthDbContext db,
        Guid? broadcasterId,
        string provider,
        string providerAccountId,
        DateTime createdAt
    )
    {
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = broadcasterId,
                Provider = provider,
                ProviderAccountId = providerAccountId,
                Status = "connected",
                CreatedAt = createdAt,
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Self-host with no bot account: the send is authored by the owner's own account, so the body's
    /// <c>sender_id</c> is the owner's Twitch user id (not null — null is what dropped the send before).
    /// </summary>
    [Fact]
    public async Task SendMessage_WithNoBotAccount_SendsAsTheOwnersOwnAccount()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await AddConnectionAsync(
            db,
            Owner,
            AuthEnums.IntegrationProvider.Twitch,
            OwnerTwitchUserId,
            DateTime.UtcNow
        );
        (HelixChatProvider provider, CapturingHelixTransport transport) = Build(db);

        await provider.SendMessageAsync(Owner, "hello chat");

        transport.LastRequest.Should().NotBeNull();
        TwitchHelixRequest sent = transport.LastRequest!;
        sent.Path.Should().Be("chat/messages");
        sent.Auth.Should().Be(TwitchHelixAuth.App);

        (string SenderId, string BroadcasterId, string Message) body = ReadBody(sent.Body!);
        body.SenderId.Should()
            .Be(OwnerTwitchUserId, "the main account is the bot until a custom bot is registered");
        body.BroadcasterId.Should().Be(OwnerTwitchChannelId);
        body.Message.Should().Be("hello chat");
    }

    /// <summary>
    /// A registered bot account takes precedence: the body's <c>sender_id</c> is the bot account's Twitch
    /// user id, never the owner's — the chat-send sender mirrors the token resolution order.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithRegisteredBotAccount_SendsAsTheBotAccount()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        await AddConnectionAsync(
            db,
            Owner,
            AuthEnums.IntegrationProvider.Twitch,
            OwnerTwitchUserId,
            DateTime.UtcNow
        );
        await AddConnectionAsync(
            db,
            null,
            AuthEnums.IntegrationProvider.Twitch + "_bot",
            BotTwitchUserId,
            DateTime.UtcNow
        );
        (HelixChatProvider provider, CapturingHelixTransport transport) = Build(db);

        await provider.SendMessageAsync(Owner, "hi from the bot");

        TwitchHelixRequest sent = (TwitchHelixRequest)transport.LastRequest!;
        (string SenderId, string BroadcasterId, string Message) body = ReadBody(sent.Body!);
        body.SenderId.Should()
            .Be(BotTwitchUserId, "a registered bot account is the chat sender when present");
        body.BroadcasterId.Should().Be(OwnerTwitchChannelId);
    }

    /// <summary>Reads the anonymous chat-send body (PascalCase) the provider hands the transport.</summary>
    private static (string SenderId, string BroadcasterId, string Message) ReadBody(object body)
    {
        System.Type t = body.GetType();
        string senderId = (string)t.GetProperty("SenderId")!.GetValue(body)!;
        string broadcasterId = (string)t.GetProperty("BroadcasterId")!.GetValue(body)!;
        string message = (string)t.GetProperty("Message")!.GetValue(body)!;
        return (senderId, broadcasterId, message);
    }
}
