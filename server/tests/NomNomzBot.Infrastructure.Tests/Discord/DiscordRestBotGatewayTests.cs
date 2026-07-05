// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Discord.Gateway;
using NomNomzBot.Infrastructure.Platform.Resilience;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the gateway really talks to Discord (discord.md §3.5): a <c>PostMessageAsync</c> issues the exact
/// Discord REST request — <c>POST https://discord.com/api/v10/channels/{id}/messages</c> with
/// <c>Authorization: Bot &lt;decrypted-token&gt;</c> and the right JSON body (content + role ping + restricted
/// allowed_mentions) — that the vaulted token decrypts to; a 429 with <c>Retry-After</c> is honored by the
/// resilience handler before succeeding; a non-2xx maps to <see cref="Result"/> failure. The token is read from
/// <see cref="IIntegrationTokenVault"/>, never a plaintext column — this is the proof the bot communicates with
/// Discord for real.
/// </summary>
public sealed class DiscordRestBotGatewayTests
{
    private const string DecryptedToken = "decrypted-bot-token-xyz";
    private static readonly Guid Channel = Guid.CreateVersion7();

    [Fact]
    public async Task PostMessageAsync_IssuesDiscordPost_WithBotAuthHeader_AndJsonBody()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"112233445566778899"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new DiscordOutboundMessage("Stream is live!", Embed: null, PingRoleId: "555444333")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("112233445566778899"); // the Discord-returned message id

        // It hit the real Discord REST endpoint for posting a channel message.
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/channels/999000111/messages");

        // The Authorization header is the bot token decrypted from the vault — "Bot <token>".
        handler.Request!.Headers.GetValues("Authorization").Should().ContainSingle();
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {DecryptedToken}");

        // The JSON body carries the content with the role ping prefixed and mentions restricted to that role.
        // (System.Text.Json HTML-escapes <, >, & — the standard, Discord-decodable form — so decode before
        // asserting on the human-readable content.)
        string decodedBody = System.Text.RegularExpressions.Regex.Unescape(handler.Body);
        decodedBody.Should().Contain("\"content\":\"<@&555444333> Stream is live!\"");
        handler.Body.Should().Contain("\"allowed_mentions\"");
        handler.Body.Should().Contain("\"roles\":[\"555444333\"]");
    }

    [Fact]
    public async Task PostMessageAsync_Honors429RetryAfter_ThenSucceeds()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        HttpResponseMessage rateLimited = new(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                """{"retry_after":0.01}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        // Discord returns Retry-After in seconds — a tiny value so the test does not stall.
        rateLimited.Headers.TryAddWithoutValidation("Retry-After", "0.01");
        HttpResponseMessage ok = new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"7777"}""", Encoding.UTF8, "application/json"),
        };
        SequencedHandler handler = new(rateLimited, ok);

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new DiscordOutboundMessage("hello", Embed: null, PingRoleId: null)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("7777");
        handler.Calls.Should().Be(2); // the 429 was retried after honoring Retry-After, then succeeded
    }

    [Fact]
    public async Task PostMessageAsync_NonSuccess_MapsToFailure()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"message":"Missing Access","code":50001}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new DiscordOutboundMessage("nope", Embed: null, PingRoleId: null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_UNAUTHORIZED");
    }

    [Fact]
    public async Task PostMessageAsync_NoDiscordConnection_FailsClosed()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        // No connection seeded → no vaulted token → must fail closed, never reaching the wire.
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        CapturingHandler handler = new(new HttpResponseMessage(HttpStatusCode.OK));

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new DiscordOutboundMessage("hi", Embed: null, PingRoleId: null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_NOT_CONNECTED");
        handler.Request.Should().BeNull(); // never hit the wire
    }

    [Fact]
    public async Task AddMemberRoleAsync_IssuesPutToGuildMemberRolesEndpoint()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(new HttpResponseMessage(HttpStatusCode.NoContent));
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result result = await gateway.AddMemberRoleAsync(Channel, "guild1", "member2", "role3");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        handler.Request!.Method.Should().Be(HttpMethod.Put);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/guilds/guild1/members/member2/roles/role3");
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {DecryptedToken}");
    }

    [Fact]
    public async Task GetGuildAsync_IssuesGetToGuildEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"guild1","name":"The Guild","icon":"a1b2c3","description":"About us","owner_id":"555"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<DiscordGuildInfoDto> result = await gateway.GetGuildAsync(Channel, "guild1");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Should()
            .Be(new DiscordGuildInfoDto("guild1", "The Guild", "a1b2c3", "About us"));

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/guilds/guild1");
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {DecryptedToken}");
    }

    [Fact]
    public async Task GetGuildRolesAsync_IssuesGetToRolesEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"role-1","name":"Notify Squad","color":16711935,"position":3,"managed":false,"hoist":true},{"id":"role-2","name":"Bot Role","color":0,"position":1,"managed":true}]""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<IReadOnlyList<DiscordGuildRoleDto>> result = await gateway.GetGuildRolesAsync(
            Channel,
            "guild1"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2);
        result
            .Value[0]
            .Should()
            .Be(new DiscordGuildRoleDto("role-1", "Notify Squad", 16711935, 3, false));
        result.Value[1].Should().Be(new DiscordGuildRoleDto("role-2", "Bot Role", 0, 1, true));

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/guilds/guild1/roles");
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {DecryptedToken}");
    }

    [Fact]
    public async Task GetGuildChannelsAsync_IssuesGetToChannelsEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"chan-1","name":"general","type":0,"parent_id":"cat-9","position":2},{"id":"cat-9","name":"Text","type":4,"parent_id":null,"position":0}]""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<IReadOnlyList<DiscordGuildChannelDto>> result = await gateway.GetGuildChannelsAsync(
            Channel,
            "guild1"
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2);
        result.Value[0].Should().Be(new DiscordGuildChannelDto("chan-1", "general", 0, "cat-9", 2));
        result.Value[1].Should().Be(new DiscordGuildChannelDto("cat-9", "Text", 4, null, 0));

        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/guilds/guild1/channels");
    }

    [Fact]
    public async Task GetGuildAsync_NonSuccess_MapsToFailure_NotThrow()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid connectionId = await SeedDiscordConnectionAsync(database);
        IIntegrationTokenVault vault = VaultReturning(connectionId, DecryptedToken);

        CapturingHandler handler = new(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"message":"Unknown Guild","code":10004}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, vault);

        Result<DiscordGuildInfoDto> result = await gateway.GetGuildAsync(Channel, "gone");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_NOT_FOUND");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordRestBotGateway NewGateway(
        HttpMessageHandler handler,
        DiscordTestDbContext db,
        IIntegrationTokenVault vault
    )
    {
        // Build the REAL named "discord" client with the production resilience handler (the same one wired in
        // DI), so the 429 Retry-After honoring is exercised end to end — over the test's primary handler.
        ServiceCollection services = new();
        services
            .AddHttpClient("discord")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddDiscordResilienceHandler();
        ServiceProvider provider = services.BuildServiceProvider();
        IHttpClientFactory factory = provider.GetRequiredService<IHttpClientFactory>();

        return new DiscordRestBotGateway(
            factory,
            db,
            vault,
            NullLogger<DiscordRestBotGateway>.Instance
        );
    }

    private static IIntegrationTokenVault VaultReturning(Guid connectionId, string token)
    {
        IIntegrationTokenVault vault = Substitute.For<IIntegrationTokenVault>();
        vault
            .GetAccessTokenAsync(connectionId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new DecryptedTokenDto(token, "access", null, false)));
        return vault;
    }

    private static async Task<Guid> SeedDiscordConnectionAsync(DiscordSqliteTestDatabase database)
    {
        Guid connectionId = Guid.CreateVersion7();
        await using DiscordTestDbContext db = database.NewContext();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                Id = connectionId,
                BroadcasterId = Channel,
                Provider = "discord",
                ProviderAccountId = "guild1",
                Status = "connected",
            }
        );
        await db.SaveChangesAsync();
        return connectionId;
    }

    /// <summary>Captures the single outbound request (method, URI, headers, body) and returns a fixed response.</summary>
    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    /// <summary>Returns each queued response in order across calls (for the 429-then-OK retry proof).</summary>
    private sealed class SequencedHandler(params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private int _index;
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            HttpResponseMessage response = responses[Math.Min(_index, responses.Length - 1)];
            _index++;
            return Task.FromResult(response);
        }
    }
}
