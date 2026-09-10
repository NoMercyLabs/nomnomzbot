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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Infrastructure.Discord.Gateway;
using NomNomzBot.Infrastructure.Platform.Resilience;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the gateway really talks to Discord (discord.md §3.5): a <c>PostMessageAsync</c> issues the exact
/// Discord REST request — <c>POST https://discord.com/api/v10/channels/{id}/messages</c> with
/// <c>Authorization: Bot &lt;the platform's configured Discord Application bot token&gt;</c> and the right JSON
/// body (content + role ping + restricted allowed_mentions) — a 429 with <c>Retry-After</c> is honored by the
/// resilience handler before succeeding; a non-2xx maps to <see cref="Result"/> failure.
///
/// <para><b>S-PL4 regression:</b> Discord's "bot guilds" OAuth authorization-code exchange returns an
/// <c>access_token</c> scoped to the authorizing USER (7-day expiry, per Discord's own OAuth2 docs default) —
/// it is never valid as <c>Authorization: Bot &lt;token&gt;</c> against guild REST endpoints
/// (<c>guilds/{id}/roles</c>, <c>guilds/{id}/channels</c>, …), no matter how freshly it was reauthorized. Only
/// the ONE static token issued to the Discord Application in the Developer Portal authenticates bot calls. The
/// gateway therefore never reads a per-channel vaulted OAuth token for these calls — it resolves the platform's
/// configured bot token (<see cref="ISystemCredentialsProvider.GetValueAsync"/>, key <c>discord.bot_token</c>)
/// once a live guild connection is confirmed to exist.</para>
/// </summary>
public sealed class DiscordRestBotGatewayTests
{
    private const string PlatformBotToken = "platform-bot-token-xyz";
    private static readonly Guid Channel = Guid.CreateVersion7();

    [Fact]
    public async Task PostMessageAsync_IssuesDiscordPost_WithBotAuthHeader_AndJsonBody()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"112233445566778899"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new("Stream is live!", Embed: null, PingRoleId: "555444333")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("112233445566778899"); // the Discord-returned message id

        // It hit the real Discord REST endpoint for posting a channel message.
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/channels/999000111/messages");

        // The Authorization header is the platform's configured bot token — "Bot <token>" — never a
        // per-channel OAuth artifact.
        handler.Request!.Headers.GetValues("Authorization").Should().ContainSingle();
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {PlatformBotToken}");

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
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

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
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new("hello", Embed: null, PingRoleId: null)
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("7777");
        handler.Calls.Should().Be(2); // the 429 was retried after honoring Retry-After, then succeeded
    }

    [Fact]
    public async Task PostMessageAsync_NonSuccess_MapsToFailure()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"message":"Missing Access","code":50001}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new("nope", Embed: null, PingRoleId: null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_UNAUTHORIZED");
    }

    [Fact]
    public async Task PostMessageAsync_NoDiscordConnection_FailsClosed()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        // No connection seeded → never reaches the wire, regardless of the platform bot token being configured.
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);
        CapturingHandler handler = new(new(HttpStatusCode.OK));

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<string> result = await gateway.PostMessageAsync(
            Channel,
            "999000111",
            new("hi", Embed: null, PingRoleId: null)
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_NOT_CONNECTED");
        handler.Request.Should().BeNull(); // never hit the wire
    }

    [Fact]
    public async Task GetGuildRolesAsync_NoPlatformBotTokenConfigured_FailsClosed_DistinctFromNotConnected()
    {
        // Regression for S-PL4 ("discord 401s everywhere, even right after a fresh reauth"). The channel can be
        // genuinely "connected" (a live guild-link row exists — the owner DID complete the OAuth dance) while the
        // platform's real Discord Application bot token is simply not configured. That must fail closed with a
        // distinct, actionable error — never by silently sending a per-channel OAuth artifact to Discord and
        // letting Discord's own opaque 401 be the only signal, which is exactly what happened in production:
        // reauthenticating produced a fresh, valid OAuth access_token (confirmed live: 7-day expiry, updated at
        // the same second the "Discord OAuth completed" log line fired) that STILL 401'd 8 seconds later, because
        // an OAuth user access_token is never a valid Bot token no matter how fresh.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithNoBotToken();

        CapturingHandler handler = new(new(HttpStatusCode.OK));
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<IReadOnlyList<DiscordGuildRoleDto>> result = await gateway.GetGuildRolesAsync(
            Channel,
            "guild1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_BOT_TOKEN_NOT_CONFIGURED");
        result.ErrorCode.Should().NotBe("DISCORD_NOT_CONNECTED"); // the connection IS live — a different fault
        handler.Request.Should().BeNull(); // never sends a credential that isn't the bot's own to Discord
    }

    [Fact]
    public async Task GetGuildRolesAsync_And_GetGuildChannelsAsync_AuthenticateWithThePlatformBotToken_NotAConnectionArtifact()
    {
        // The two exact endpoints that 401'd in production (guild/roles, guild/channels), proven to authenticate
        // with the platform's configured bot token even though the connection row looks freshly reauthorized —
        // there is no per-connection credential in play at all for these calls.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler rolesHandler = new(
            JsonResponse(
                """[{"id":"role-1","name":"Live","color":0,"position":1,"managed":false}]"""
            )
        );
        await using (DiscordTestDbContext rolesDb = database.NewContext())
        {
            DiscordRestBotGateway rolesGateway = NewGateway(rolesHandler, rolesDb, credentials);
            Result<IReadOnlyList<DiscordGuildRoleDto>> roles =
                await rolesGateway.GetGuildRolesAsync(Channel, "guild1");
            roles.IsSuccess.Should().BeTrue(roles.ErrorMessage);
        }
        rolesHandler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {PlatformBotToken}");

        CapturingHandler channelsHandler = new(
            JsonResponse("""[{"id":"chan-1","name":"general","type":0,"position":0}]""")
        );
        await using (DiscordTestDbContext channelsDb = database.NewContext())
        {
            DiscordRestBotGateway channelsGateway = NewGateway(
                channelsHandler,
                channelsDb,
                credentials
            );
            Result<IReadOnlyList<DiscordGuildChannelDto>> channels =
                await channelsGateway.GetGuildChannelsAsync(Channel, "guild1");
            channels.IsSuccess.Should().BeTrue(channels.ErrorMessage);
        }
        channelsHandler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {PlatformBotToken}");
    }

    [Fact]
    public async Task OpenDmChannelAsync_PostsRecipientIdToUsersMeChannels_AndReturnsChannelId()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"dm-chan-42","type":1}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<string> result = await gateway.OpenDmChannelAsync(Channel, "member-777");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().Be("dm-chan-42"); // the DM channel id, to be cached and posted to

        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler
            .Request!.RequestUri!.ToString()
            .Should()
            .Be("https://discord.com/api/v10/users/@me/channels");
        handler
            .Request!.Headers.GetValues("Authorization")
            .Single()
            .Should()
            .Be($"Bot {PlatformBotToken}");
        handler.Body.Should().Contain("\"recipient_id\":\"member-777\"");
    }

    [Fact]
    public async Task AddMemberRoleAsync_IssuesPutToGuildMemberRolesEndpoint()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(new(HttpStatusCode.NoContent));
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

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
            .Be($"Bot {PlatformBotToken}");
    }

    [Fact]
    public async Task ValidateRoleAssignableAsync_Succeeds_WhenBotHasManageRolesAboveTarget()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":5,"managed":false,"permissions":"268435456"},{"id":"live-role","name":"Live","color":0,"position":2,"managed":false,"permissions":"0"}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result result = await gateway.ValidateRoleAssignableAsync(Channel, "guild1", "live-role");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateRoleAssignableAsync_Fails_WithActionableMessage_WhenManageRolesMissing()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":5,"managed":false,"permissions":"0"},{"id":"live-role","name":"Live","color":0,"position":2,"managed":false,"permissions":"0"}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result result = await gateway.ValidateRoleAssignableAsync(Channel, "guild1", "live-role");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_MISSING_MANAGE_ROLES");
        result
            .ErrorMessage.Should()
            .Be(
                "The bot needs the Manage Roles permission in this Discord server to manage the "
                    + "live role. Grant Manage Roles to the bot's role in Server Settings > Roles."
            );
    }

    [Fact]
    public async Task ValidateRoleAssignableAsync_Fails_WithActionableMessage_WhenBotRoleBelowTarget()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":1,"managed":false,"permissions":"268435456"},{"id":"live-role","name":"Live","color":0,"position":5,"managed":false,"permissions":"0"}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result result = await gateway.ValidateRoleAssignableAsync(Channel, "guild1", "live-role");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_ROLE_HIERARCHY");
        result
            .ErrorMessage.Should()
            .Be(
                "The bot's role must be above 'Live' in Server Settings > Roles for the bot to "
                    + "apply or remove the live role."
            );
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_FlagsRoleAboveBot_Assignable_AndRoleBelow_NotAssignable_WithHierarchyReason()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        // Bot role at position 3 with Manage Roles (268435456). "below-role" sits under the bot (position 1) —
        // assignable. "above-role" sits over the bot (position 5) — not assignable, hierarchy reason.
        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":3,"managed":false,"permissions":"268435456"},{"id":"below-role","name":"Below","color":0,"position":1,"managed":false,"permissions":"0"},{"id":"above-role","name":"Above","color":0,"position":5,"managed":false,"permissions":"0"}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> result =
            await gateway.GetAssignableGuildRolesAsync(Channel, "guild1");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        DiscordAssignableRoleDto below = result.Value.Single(r => r.Id == "below-role");
        DiscordAssignableRoleDto above = result.Value.Single(r => r.Id == "above-role");

        below.CanAssign.Should().BeTrue();
        below.UnavailableReasonCode.Should().BeNull();
        below.UnavailableReason.Should().BeNull();

        above.CanAssign.Should().BeFalse();
        above.UnavailableReasonCode.Should().Be("DISCORD_ROLE_HIERARCHY");
        above
            .UnavailableReason.Should()
            .Be(
                "The bot's role must be above 'Above' in Server Settings > Roles for the bot to "
                    + "apply or remove the live role."
            );
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_MissingManageRoles_FlagsEveryRole_WithThatReason()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":5,"managed":false,"permissions":"0"},{"id":"role-a","name":"A","color":0,"position":1,"managed":false,"permissions":"0"},{"id":"role-b","name":"B","color":0,"position":2,"managed":false,"permissions":"0"}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> result =
            await gateway.GetAssignableGuildRolesAsync(Channel, "guild1");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Where(r => r.Id is "role-a" or "role-b")
            .Should()
            .OnlyContain(r =>
                !r.CanAssign && r.UnavailableReasonCode == "DISCORD_MISSING_MANAGE_ROLES"
            );
    }

    [Fact]
    public async Task GetPostableGuildChannelsAsync_ChannelOverwriteDenyingSendMessages_WinsOverGuildLevelAllow()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        // Bot's role grants View Channel + Send Messages at guild level (0xc00 = 3072). The "locked" channel
        // carries a role-overwrite denying Send Messages (0x800) for that same role — the overwrite must win
        // even though the guild-level role permission would otherwise allow posting. The "open" channel has no
        // overwrite touching the bot's role, so guild-level permissions apply unmodified.
        SequencedHandler handler = new(
            JsonResponse("""{"id":"bot-id-1"}"""),
            JsonResponse("""{"roles":["bot-role"]}"""),
            JsonResponse(
                """[{"id":"guild1","name":"@everyone","color":0,"position":0,"managed":false,"permissions":"0"},{"id":"bot-role","name":"Bot","color":0,"position":3,"managed":false,"permissions":"3072"}]"""
            ),
            JsonResponse(
                """[{"id":"locked-chan","name":"locked","type":0,"position":0,"permission_overwrites":[{"id":"bot-role","type":0,"allow":"0","deny":"2048"}]},{"id":"open-chan","name":"open","type":0,"position":1,"permission_overwrites":[]}]"""
            )
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<IReadOnlyList<DiscordPostableChannelDto>> result =
            await gateway.GetPostableGuildChannelsAsync(Channel, "guild1");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        DiscordPostableChannelDto locked = result.Value.Single(c => c.Id == "locked-chan");
        DiscordPostableChannelDto open = result.Value.Single(c => c.Id == "open-chan");

        locked.CanPost.Should().BeFalse(); // overwrite denies Send Messages despite guild-level allow
        locked.UnavailableReasonCode.Should().Be("DISCORD_CHANNEL_PERMISSION_DENIED");
        locked.UnavailableReason.Should().Contain("Send Messages").And.Contain("locked");

        open.CanPost.Should().BeTrue(); // no overwrite touches the bot's role — guild-level permissions apply
        open.UnavailableReasonCode.Should().BeNull();
    }

    [Fact]
    public async Task GetAssignableGuildRolesAsync_DiscordUnreachable_FailsDistinctly_NotAnEmptyList()
    {
        // The "Discord cannot be used right now" state must be distinct on the wire from "there are none": a
        // non-2xx from Discord's own API maps to Result.Failure, never Result.Success([]) — the client can tell
        // "unavailable" apart from "empty" by checking IsSuccess before ever looking at the list.
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"message":"Missing Access","code":50001}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<IReadOnlyList<DiscordAssignableRoleDto>> result =
            await gateway.GetAssignableGuildRolesAsync(Channel, "guild1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_UNAUTHORIZED");
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task GetGuildAsync_IssuesGetToGuildEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"guild1","name":"The Guild","icon":"a1b2c3","description":"About us","owner_id":"555"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

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
            .Be($"Bot {PlatformBotToken}");
    }

    [Fact]
    public async Task GetGuildRolesAsync_IssuesGetToRolesEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"role-1","name":"Notify Squad","color":16711935,"position":3,"managed":false,"hoist":true},{"id":"role-2","name":"Bot Role","color":0,"position":1,"managed":true}]""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

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
            .Be($"Bot {PlatformBotToken}");
    }

    [Fact]
    public async Task GetGuildChannelsAsync_IssuesGetToChannelsEndpoint_AndMapsEveryField()
    {
        using DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"chan-1","name":"general","type":0,"parent_id":"cat-9","position":2},{"id":"cat-9","name":"Text","type":4,"parent_id":null,"position":0}]""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

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
        await SeedDiscordConnectionAsync(database);
        ISystemCredentialsProvider credentials = CredentialsWithBotToken(PlatformBotToken);

        CapturingHandler handler = new(
            new(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"message":"Unknown Guild","code":10004}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );

        await using DiscordTestDbContext db = database.NewContext();
        DiscordRestBotGateway gateway = NewGateway(handler, db, credentials);

        Result<DiscordGuildInfoDto> result = await gateway.GetGuildAsync(Channel, "gone");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISCORD_NOT_FOUND");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DiscordRestBotGateway NewGateway(
        HttpMessageHandler handler,
        DiscordTestDbContext db,
        ISystemCredentialsProvider credentials
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

        return new(factory, db, credentials, NullLogger<DiscordRestBotGateway>.Instance);
    }

    /// <summary>A platform bot token IS configured (the healthy state) — resolves for the <c>discord</c>
    /// provider's <c>bot_token</c> field, exactly as <see cref="ISystemCredentialsProvider.GetValueAsync"/>
    /// resolves any other system-scoped value.</summary>
    private static ISystemCredentialsProvider CredentialsWithBotToken(string token)
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetValueAsync("discord", "bot_token", Arg.Any<CancellationToken>())
            .Returns(token);
        return credentials;
    }

    /// <summary>No platform bot token configured (DB row absent, no env/appsettings fallback) — the state a
    /// fresh install or a mis-set <c>.env</c> leaves the operator in.</summary>
    private static ISystemCredentialsProvider CredentialsWithNoBotToken()
    {
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetValueAsync("discord", "bot_token", Arg.Any<CancellationToken>())
            .Returns((string?)null);
        return credentials;
    }

    private static async Task SeedDiscordConnectionAsync(DiscordSqliteTestDatabase database)
    {
        await using DiscordTestDbContext db = database.NewContext();
        db.IntegrationConnections.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Provider = "discord",
                ProviderAccountId = "guild1",
                Status = "connected",
            }
        );
        await db.SaveChangesAsync();
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
