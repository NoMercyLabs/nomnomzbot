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
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
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
        AuthDbContext db,
        ITwitchIdentityResolver? identityResolver = null
    )
    {
        CapturingHelixTransport transport = new();
        HelixChatProvider provider = new(
            transport,
            Substitute.For<ITwitchModerationApi>(),
            identityResolver ?? new StubIdentityResolver(Owner, OwnerTwitchChannelId),
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
    /// <c>sender_id</c> is the owner's Twitch user id (not null — null is what dropped the send before). The
    /// owner-as-bot rides that channel's OWN broadcaster token (<see cref="TwitchHelixAuth.User"/> for this
    /// tenant), so the sender and the signing token belong to the same account.
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
        sent.Auth.Should()
            .Be(
                TwitchHelixAuth.User,
                "the owner-as-bot rides its own channel's broadcaster token, not a global app token"
            );
        sent.BroadcasterId.Should()
            .Be(Owner, "the transport must resolve THIS tenant's token to sign the send");

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
        sent.Auth.Should()
            .Be(
                TwitchHelixAuth.App,
                "the shared platform bot rides the app/bot token, not a tenant token"
            );
        sent.BroadcasterId.Should()
            .BeNull("the shared bot is subject-agnostic — its token is not scoped to a tenant");
        (string SenderId, string BroadcasterId, string Message) body = ReadBody(sent.Body!);
        body.SenderId.Should()
            .Be(BotTwitchUserId, "a registered bot account is the chat sender when present");
        body.BroadcasterId.Should().Be(OwnerTwitchChannelId);
    }

    /// <summary>
    /// Multi-tenant correctness: on a deployment with several channels and NO shared bot, each channel's send
    /// is authored by THAT channel's own owner account — never one global (oldest) account for everyone. The
    /// sender_id differs per broadcaster AND each send rides its own channel's broadcaster token
    /// (<see cref="TwitchHelixAuth.User"/> for that tenant), so sender and token always match at Twitch.
    /// </summary>
    [Fact]
    public async Task SendMessage_AcrossChannels_SendsAsEachChannelsOwnAccount()
    {
        Guid channelA = Guid.Parse("0192a000-0000-7000-8000-00000000aaa1");
        Guid channelB = Guid.Parse("0192a000-0000-7000-8000-00000000bbb2");
        const string channelATwitchId = "channel-a-11";
        const string channelBTwitchId = "channel-b-22";
        const string ownerAUserId = "owner-a-user";
        const string ownerBUserId = "owner-b-user";

        AuthDbContext db = AuthTestBuilder.NewContext();
        // Channel A is created FIRST: the old global "oldest twitch connection" fallback would make BOTH
        // channels send as A's account. The fix must instead pick each channel's OWN owner.
        await AddConnectionAsync(
            db,
            channelA,
            AuthEnums.IntegrationProvider.Twitch,
            ownerAUserId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await AddConnectionAsync(
            db,
            channelB,
            AuthEnums.IntegrationProvider.Twitch,
            ownerBUserId,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        MultiChannelIdentityResolver resolver = new(
            new Dictionary<Guid, string>
            {
                [channelA] = channelATwitchId,
                [channelB] = channelBTwitchId,
            }
        );
        (HelixChatProvider provider, CapturingHelixTransport transport) = Build(db, resolver);

        await provider.SendMessageAsync(channelA, "from A");
        TwitchHelixRequest sentA = transport.LastRequest!;
        (string SenderId, string BroadcasterId, string Message) bodyA = ReadBody(sentA.Body!);

        await provider.SendMessageAsync(channelB, "from B");
        TwitchHelixRequest sentB = transport.LastRequest!;
        (string SenderId, string BroadcasterId, string Message) bodyB = ReadBody(sentB.Body!);

        // Each channel sends as its OWN owner — two DIFFERENT sender ids, not one global account.
        bodyA.SenderId.Should().Be(ownerAUserId);
        bodyB.SenderId.Should().Be(ownerBUserId);
        bodyA
            .SenderId.Should()
            .NotBe(
                bodyB.SenderId,
                "the bot sender must resolve per broadcaster, not one global id"
            );

        // Each send targets and is signed for its own channel — sender_id matches the resolved token.
        bodyA.BroadcasterId.Should().Be(channelATwitchId);
        bodyB.BroadcasterId.Should().Be(channelBTwitchId);
        sentA.Auth.Should().Be(TwitchHelixAuth.User);
        sentB.Auth.Should().Be(TwitchHelixAuth.User);
        sentA.BroadcasterId.Should().Be(channelA, "A's send resolves A's broadcaster token");
        sentB.BroadcasterId.Should().Be(channelB, "B's send resolves B's broadcaster token");
    }

    /// <summary>The send reports its REAL outcome: when Helix accepts the message, it returns <c>true</c>.</summary>
    [Fact]
    public async Task SendMessage_WhenHelixAcceptsTheMessage_ReturnsTrue()
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
        transport.SendResult = Result.Success();

        bool sent = await provider.SendMessageAsync(Owner, "hello chat");

        sent.Should().BeTrue("Helix accepted the send");
    }

    /// <summary>
    /// The honesty guarantee: a swallowed Helix rejection (e.g. a dead/expired token) makes the send return
    /// <c>false</c>, not a silent success — so the dashboard send path reports the failure instead of lying.
    /// </summary>
    [Fact]
    public async Task SendMessage_WhenHelixRejectsTheSend_ReturnsFalse()
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
        transport.SendResult = Result.Failure("Invalid OAuth token", "TWITCH_ERROR");

        bool sent = await provider.SendMessageAsync(Owner, "hello chat");

        sent.Should().BeFalse("a rejected Helix send must not masquerade as success");
        transport.LastRequest.Should().NotBeNull("the send was attempted before it failed");
    }

    /// <summary>
    /// No Twitch connection for the channel: the send can't resolve a channel id, so it returns <c>false</c>
    /// WITHOUT ever hitting the transport — the caller learns the send didn't happen.
    /// </summary>
    [Fact]
    public async Task SendMessage_WithNoTwitchConnection_ReturnsFalseWithoutCallingHelix()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        (HelixChatProvider provider, CapturingHelixTransport transport) = Build(db);

        bool sent = await provider.SendMessageAsync(
            Guid.Parse("0192a000-0000-7000-8000-0000000000ff"),
            "hello chat"
        );

        sent.Should().BeFalse("there is no Twitch connection to send through");
        transport.CallCount.Should().Be(0, "the send short-circuits before the Helix call");
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

    /// <summary>An <see cref="ITwitchIdentityResolver"/> over a fixed tenant Guid → Twitch channel id map.</summary>
    private sealed class MultiChannelIdentityResolver(IReadOnlyDictionary<Guid, string> channels)
        : ITwitchIdentityResolver
    {
        public Task<string?> GetTwitchChannelIdAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(channels.TryGetValue(broadcasterId, out string? id) ? id : null);

        public Task<Guid?> GetBroadcasterIdAsync(
            string twitchChannelId,
            CancellationToken ct = default
        ) => Task.FromResult<Guid?>(null);

        public Task<Guid?> GetBroadcasterIdByNameAsync(
            string channelName,
            CancellationToken ct = default
        ) => Task.FromResult<Guid?>(null);

        public Task<string?> GetTwitchUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }
}
