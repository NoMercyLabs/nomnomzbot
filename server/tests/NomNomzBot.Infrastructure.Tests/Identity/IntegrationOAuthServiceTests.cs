// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using ConfigEntity = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the generic, descriptor-driven OAuth connect flow (integrations-oauth §3.1, §3.2): the registry
/// resolves a provider's endpoints + full scope-set surface; <c>StartConnectAsync</c> builds the authorize
/// URL from the descriptor with the requested scope-set, a PKCE S256 challenge, and a single-use state; and
/// <c>HandleCallbackAsync</c> exchanges the code for tokens (mocked HTTP) and persists them through the
/// identity-auth vault — fail-closed on a replayed/invalid state.
/// </summary>
public sealed class IntegrationOAuthServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000f6");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-0000000000f7");

    // ─── registry: a provider is a descriptor ──────────────────────────────────

    [Fact]
    public void Registry_ResolvesSpotify_WithItsScopeSets()
    {
        OAuthProviderRegistry registry = new(EmptyConfig());

        Result<OAuthProviderDescriptor> spotify = registry.Resolve(
            AuthEnums.IntegrationProvider.Spotify,
            Tenant
        );

        spotify.IsSuccess.Should().BeTrue();
        spotify.Value.AuthorizeEndpoint.Should().Be("https://accounts.spotify.com/authorize");
        spotify.Value.UsesPkce.Should().BeTrue();
        spotify.Value.ScopeSets.Should().ContainKey("spotify.playback");
        spotify.Value.ScopeSets["spotify.playback"].Should().Contain("user-modify-playback-state");
        spotify.Value.ScopeSets.Should().ContainKey("spotify.library");
    }

    [Fact]
    public void Registry_UnknownProvider_Fails()
    {
        OAuthProviderRegistry registry = new(EmptyConfig());
        registry.Resolve("myspace", Tenant).ErrorCode.Should().Be("UNKNOWN_PROVIDER");
    }

    /// <summary>
    /// Proves the Patreon descriptor (supporter-events.md OAuth-vault connect; endpoints verified against
    /// docs.patreon.com): a confidential client (no PKCE), the JSON:API identity endpoint, webhook
    /// management (<c>w:campaigns.webhook</c>) riding the supporters core set, and member PII split into
    /// its own opt-in set — never bundled into the core grant.
    /// </summary>
    [Fact]
    public void Registry_ResolvesPatreon_WithVerifiedEndpointsAndSplitPiiScopes()
    {
        OAuthProviderRegistry registry = new(EmptyConfig());

        Result<OAuthProviderDescriptor> patreon = registry.Resolve(
            AuthEnums.IntegrationProvider.Patreon,
            Tenant
        );

        patreon.IsSuccess.Should().BeTrue();
        patreon.Value.AuthorizeEndpoint.Should().Be("https://www.patreon.com/oauth2/authorize");
        patreon.Value.TokenEndpoint.Should().Be("https://www.patreon.com/api/oauth2/token");
        patreon
            .Value.AccountIdentityEndpoint.Should()
            .Be("https://www.patreon.com/api/oauth2/v2/identity");
        patreon.Value.UsesPkce.Should().BeFalse("Patreon documents a confidential client, no PKCE");
        patreon.Value.ScopeSets["patreon.supporters"].Should().Contain("w:campaigns.webhook");
        patreon
            .Value.ScopeSets["patreon.supporters"]
            .Should()
            .NotContain(
                "campaigns.members[email]",
                "member PII is an explicit opt-in set, never bundled"
            );
        patreon.Value.ScopeSets.Should().ContainKey("patreon.members_pii");
    }

    /// <summary>
    /// Proves the identity probe reads Patreon's JSON:API envelope — the id on the <c>data</c> OBJECT (not
    /// array) and the display name nested under <c>attributes</c> — so a Patreon connect stores a real
    /// account identity instead of nulls.
    /// </summary>
    [Fact]
    public async Task HandleCallback_ForPatreon_ReadsTheJsonApiIdentity()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"patreon-access","refresh_token":"patreon-refresh","expires_in":3600,"scope":"identity campaigns campaigns.members w:campaigns.webhook"}""",
            IdentityJson =
                """{"data":{"id":"patreon-user-9","type":"user","attributes":{"full_name":"Pat Ron"}},"links":{"self":"https://www.patreon.com/api/oauth2/v2/user/9"}}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Patreon,
            "patreon.supporters",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        start.IsSuccess.Should().BeTrue();

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Patreon,
            new("the-auth-code", start.Value.State, null, null)
        );

        callback.IsSuccess.Should().BeTrue();
        callback.Value.ProviderAccountName.Should().Be("Pat Ron");
        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Provider.Should().Be(AuthEnums.IntegrationProvider.Patreon);
        connection.ProviderAccountId.Should().Be("patreon-user-9");
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        // A confidential client: the exchange carries the secret, never a PKCE verifier.
        handler.LastTokenRequestBody.Should().Contain("client_secret=patreon-secret");
        handler.LastTokenRequestBody.Should().NotContain("code_verifier=");
    }

    /// <summary>
    /// Proves an identity-less provider (TreatStream — no identity endpoint exists, verified against
    /// treatstream.com/api/details) connects cleanly: the callback vaults the tokens and stores the
    /// connection WITHOUT probing a nonexistent identity route (the identity request is never made).
    /// </summary>
    [Fact]
    public async Task HandleCallback_ForTreatstream_ConnectsIdentityLess_WithoutProbing()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"ts-access","refresh_token":"ts-refresh","expires_in":3600,"scope":"userinfo"}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Treatstream,
            "treatstream.treats",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        start.IsSuccess.Should().BeTrue(start.ErrorMessage);
        start.Value.AuthorizeUrl.Should().StartWith("https://treatstream.com/Oauth2/Authorize");

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Treatstream,
            new("the-auth-code", start.Value.State, null, null)
        );

        callback.IsSuccess.Should().BeTrue(callback.ErrorMessage);
        handler.LastIdentityRequestUri.Should().BeNull("there is no identity endpoint to probe");
        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Provider.Should().Be(AuthEnums.IntegrationProvider.Treatstream);
        connection.ProviderAccountId.Should().BeNull("the connect is identity-less by design");
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
    }

    // ─── StartConnect: descriptor drives the URL ───────────────────────────────

    [Fact]
    public async Task StartConnect_BuildsAuthorizeUrl_WithScopeSetStateAndPkce()
    {
        (IntegrationOAuthService service, _, _, FakeCache cache) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            returnUrl: "https://dash.example/return",
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.IsSuccess.Should().BeTrue();
        string url = start.Value.AuthorizeUrl;
        url.Should().StartWith("https://accounts.spotify.com/authorize");
        url.Should().Contain("client_id=spotify-client");
        url.Should().Contain("response_type=code");
        url.Should().Contain("code_challenge_method=S256");
        url.Should().Contain($"state={start.Value.State}");
        Uri.UnescapeDataString(url).Should().Contain("user-modify-playback-state");

        // The redirect_uri is built from the request's public origin (the tunnel/domain) — NEVER localhost, which
        // Spotify rejects outright. It is what the owner registers and what the bot sends, identically.
        Uri.UnescapeDataString(url)
            .Should()
            .Contain(
                "redirect_uri=https://bot-dev.nomercy.tv/api/v1/integrations/spotify/callback"
            );
        Uri.UnescapeDataString(url).Should().NotContain("localhost");

        // The verifier + binding were stashed single-use under the state key.
        cache.Contains($"oauth:state:{start.Value.State}").Should().BeTrue();
    }

    [Fact]
    public async Task StartConnect_UnknownScopeSet_Fails()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.bogus",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.ErrorCode.Should().Be("UNKNOWN_SCOPE_SET");
    }

    /// <summary>A '+'-joined scope-set key requests the union of every named set in one authorize call.</summary>
    [Fact]
    public async Task StartConnect_CompositeScopeSetKey_RequestsTheUnionOfBothSets()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback+spotify.streaming",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.IsSuccess.Should().BeTrue(start.ErrorMessage);
        string scope = Uri.UnescapeDataString(start.Value.AuthorizeUrl);
        scope.Should().Contain("user-modify-playback-state");
        scope.Should().Contain("streaming");
        scope.Should().Contain("user-read-email");
    }

    [Fact]
    public async Task StartConnect_CompositeScopeSetKey_WithOneUnknownMember_Fails()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback+spotify.bogus",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.ErrorCode.Should().Be("UNKNOWN_SCOPE_SET");
    }

    /// <summary>
    /// Proves the shop-scoped connect (Shopify, supporter-events.md OAuth-vault): the shop name is required
    /// and sanitized (a pasted <c>Name.myshopify.com</c> works; a URL is rejected), every endpoint resolves
    /// onto the shop's domain, the identity ride's Shopify's own <c>X-Shopify-Access-Token</c> header (never
    /// Bearer) and reads the <c>shop</c> envelope, and the connection remembers its shop domain for later
    /// provider API calls.
    /// </summary>
    [Fact]
    public async Task ShopifyConnect_ResolvesShopScopedEndpoints_AndRemembersTheShop()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"shpat-token","refresh_token":"shpat-refresh","expires_in":86400,"scope":"read_orders"}""",
            IdentityJson =
                """{"shop":{"id":548380009,"name":"My Test Store","myshopify_domain":"my-store.myshopify.com"}}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Shopify,
            "shopify.orders",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv",
            shopDomain: "My-Store.myshopify.com" // pasted full domain, mixed case — sanitized
        );

        start.IsSuccess.Should().BeTrue(start.ErrorMessage);
        start
            .Value.AuthorizeUrl.Should()
            .StartWith("https://my-store.myshopify.com/admin/oauth/authorize");
        start.Value.AuthorizeUrl.Should().Contain("client_id=shopify-client");
        start
            .Value.AuthorizeUrl.Should()
            .NotContain("code_challenge", "Shopify's grant has no PKCE");

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Shopify,
            new("the-auth-code", start.Value.State, null, null)
        );

        callback.IsSuccess.Should().BeTrue(callback.ErrorMessage);
        callback.Value.ProviderAccountName.Should().Be("My Test Store");
        // The exchange + identity both resolved onto the SHOP's domain…
        handler.LastTokenRequestUri!.Host.Should().Be("my-store.myshopify.com");
        handler.LastIdentityRequestUri!.Host.Should().Be("my-store.myshopify.com");
        // …with Shopify's own token header, never Bearer.
        handler.LastIdentityShopifyHeader.Should().Be("shpat-token");

        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Provider.Should().Be(AuthEnums.IntegrationProvider.Shopify);
        connection
            .Settings.Should()
            .Contain("my-store", "later provider API calls need the shop domain");
    }

    [Fact]
    public async Task ShopifyConnect_WithoutAShop_FailsActionably()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Shopify,
            "shopify.orders",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.ErrorCode.Should().Be("SHOP_REQUIRED");
    }

    [Fact]
    public async Task ShopifyConnect_WithAUrlAsShop_IsRejected_NeverSubstituted()
    {
        // The {shop} substitution must never become an SSRF vector — a URL/host injection is invalid input.
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Shopify,
            "shopify.orders",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv",
            shopDomain: "evil.example.com/steal?x="
        );

        start.ErrorCode.Should().Be("SHOP_REQUIRED");
    }

    // ─── HandleCallback: code → tokens → vault ─────────────────────────────────

    [Fact]
    public async Task HandleCallback_ExchangesCode_AndVaultsTokens()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"spotify-access","refresh_token":"spotify-refresh","expires_in":3600,"scope":"user-read-playback-state user-modify-playback-state user-read-currently-playing"}""",
            IdentityJson = """{"id":"spotify-user-1","display_name":"DJ Test"}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, IIntegrationTokenVault vault, _) =
            Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new("the-auth-code", start.Value.State, null, null)
        );

        callback.IsSuccess.Should().BeTrue();
        callback.Value.Provider.Should().Be(AuthEnums.IntegrationProvider.Spotify);
        callback.Value.ProviderAccountName.Should().Be("DJ Test");
        callback.Value.GrantedScopeSets.Should().Contain("spotify.playback");

        // The connection + a sealed access token are persisted via the vault.
        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Provider.Should().Be(AuthEnums.IntegrationProvider.Spotify);
        connection.ProviderAccountId.Should().Be("spotify-user-1");
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);

        Result<DecryptedTokenDto> access = await vault.GetAccessTokenAsync(connection.Id);
        access.Value.Value.Should().Be("spotify-access");

        // The exchange used the provider token endpoint with the auth code.
        handler.LastTokenRequestBody.Should().Contain("code=the-auth-code");
        handler.LastTokenRequestBody.Should().Contain("grant_type=authorization_code");
        handler.LastTokenRequestBody.Should().Contain("code_verifier=");

        // The token exchange's redirect_uri is the public-origin URL persisted at /connect — matching the
        // authorize request byte-for-byte (OAuth requires it), and never the loopback Spotify would reject.
        Uri.UnescapeDataString(handler.LastTokenRequestBody!)
            .Should()
            .Contain(
                "redirect_uri=https://bot-dev.nomercy.tv/api/v1/integrations/spotify/callback"
            );
    }

    // ─── Disconnect: Kick tells the platform to stop delivering webhooks ───────

    /// <summary>
    /// S028 (Kick hygiene): disconnecting a Kick connection must stop Kick's webhook deliveries, not
    /// just revoke the local connection row. Before revoking, DisconnectAsync lists the connection's
    /// live subscriptions and removes them by id — proven here by asserting the FAKE client actually
    /// received the two subscription ids Kick reported, not merely that some unsubscribe happened.
    /// </summary>
    [Fact]
    public async Task Disconnect_ForKick_UnsubscribesFromKicksWebhooks_BeforeRevoking()
    {
        FakeKickApiClient kick = new([
            new KickEventSubscription("sub-1", "chat.message.sent", 1, "webhook", 42),
            new KickEventSubscription("sub-2", "channel.followed", 1, "webhook", 42),
        ]);
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"kick-access","refresh_token":"kick-refresh","expires_in":3600,"scope":"user:read chat:write moderation:ban moderation:chat_message:manage events:subscribe"}""",
            IdentityJson = """{"data":[{"user_id":42,"name":"KickStreamer"}]}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, IIntegrationTokenVault vault, _) =
            Build(handler, new FakeDiscordGuildService(), kick);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Kick,
            "kick.chat",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        start.IsSuccess.Should().BeTrue(start.ErrorMessage);

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Kick,
            new("the-auth-code", start.Value.State, null, null)
        );
        callback.IsSuccess.Should().BeTrue(callback.ErrorMessage);

        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Provider == AuthEnums.IntegrationProvider.Kick);
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);

        Result disconnect = await service.DisconnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Kick,
            Actor
        );
        disconnect.IsSuccess.Should().BeTrue(disconnect.ErrorMessage);

        // The exact ids Kick reported were sent to unsubscribe — not a placeholder, not "all", the
        // real subscription set for THIS connection's token.
        kick.UnsubscribeCalls.Should().ContainSingle();
        kick.UnsubscribeCalls[0].Should().BeEquivalentTo(["sub-1", "sub-2"]);

        // The connection itself is revoked, exactly like every other provider's disconnect.
        IntegrationConnection revoked = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Id == connection.Id);
        revoked.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);
    }

    // ─── Kick bot account (kick_bot): its own persisted connection, never the streamer's `kick` row ────

    /// <summary>
    /// Proves the Kick BOT account connect (kick_bot descriptor, the same shape as ChannelBotController's
    /// Twitch white-label bot, adapted to Kick's actual auth-code+PKCE mechanics — Kick has no device-code
    /// grant): completing the callback persists a SEPARATE <c>kick_bot</c> connection row under the SAME
    /// tenant, and does not touch/overwrite a pre-existing streamer `kick` connection for that same tenant.
    /// </summary>
    [Fact]
    public async Task HandleCallback_ForKickBot_PersistsSeparateConnection_NeverOverwritingTheStreamersKick()
    {
        // Seed the streamer's own, already-connected Kick platform connection first.
        StubHandler streamerHandler = new()
        {
            TokenJson =
                """{"access_token":"streamer-access","refresh_token":"streamer-refresh","expires_in":3600,"scope":"user:read chat:write moderation:ban moderation:chat_message:manage events:subscribe"}""",
            IdentityJson = """{"data":[{"user_id":42,"name":"KickStreamer"}]}""",
        };
        (IntegrationOAuthService streamerService, AuthDbContext db, _, _) = Build(streamerHandler);
        Result<OAuthStartDto> streamerStart = await streamerService.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Kick,
            "kick.chat",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        Result<OAuthCallbackResultDto> streamerCallback = await streamerService.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Kick,
            new("streamer-auth-code", streamerStart.Value.State, null, null)
        );
        streamerCallback.IsSuccess.Should().BeTrue(streamerCallback.ErrorMessage);

        // Now connect the BOT account — a DIFFERENT Kick account authorizing through the SAME app.
        StubHandler botHandler = new()
        {
            TokenJson =
                """{"access_token":"bot-access","refresh_token":"bot-refresh","expires_in":3600,"scope":"user:read chat:write moderation:ban moderation:chat_message:manage events:subscribe"}""",
            IdentityJson = """{"data":[{"user_id":99,"name":"MyStreamBot"}]}""",
        };
        (IntegrationOAuthService botService, AuthDbContext botDb, IIntegrationTokenVault vault, _) =
            BuildOnSameDb(db, botHandler);

        Result<OAuthStartDto> botStart = await botService.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.KickBot,
            "kick_bot.chat",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        botStart.IsSuccess.Should().BeTrue(botStart.ErrorMessage);
        // Credentials resolve under the PARENT "kick" key — there is no separate KICK_BOT_CLIENT_ID/SECRET.
        botStart.Value.AuthorizeUrl.Should().Contain("client_id=kick-client");

        Result<OAuthCallbackResultDto> botCallback = await botService.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.KickBot,
            new("bot-auth-code", botStart.Value.State, null, null)
        );
        botCallback.IsSuccess.Should().BeTrue(botCallback.ErrorMessage);

        // Two SEPARATE rows for the same tenant — the streamer's `kick` connection is untouched.
        IntegrationConnection streamerConnection = await botDb
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Provider == AuthEnums.IntegrationProvider.Kick);
        streamerConnection.ProviderAccountId.Should().Be("42");
        streamerConnection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);

        IntegrationConnection botConnection = await botDb
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Provider == AuthEnums.IntegrationProvider.KickBot);
        botConnection.BroadcasterId.Should().Be(Tenant);
        botConnection.ProviderAccountId.Should().Be("99");
        botConnection.ProviderAccountName.Should().Be("MyStreamBot");
        botConnection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        botConnection.Id.Should().NotBe(streamerConnection.Id);

        // The bot's own token round-trips through the vault under its own connection id.
        Result<DecryptedTokenDto> botAccess = await vault.GetAccessTokenAsync(botConnection.Id);
        botAccess.Value.Value.Should().Be("bot-access");
    }

    /// <summary>
    /// Proves disconnecting the Kick bot account removes ONLY the <c>kick_bot</c> connection row — the
    /// streamer's own `kick` connection for the same tenant is left connected and untouched.
    /// </summary>
    [Fact]
    public async Task Disconnect_ForKickBot_RemovesOnlyTheBotConnection_LeavingTheStreamersKickConnected()
    {
        StubHandler streamerHandler = new()
        {
            TokenJson =
                """{"access_token":"streamer-access","refresh_token":"streamer-refresh","expires_in":3600,"scope":"user:read chat:write moderation:ban moderation:chat_message:manage events:subscribe"}""",
            IdentityJson = """{"data":[{"user_id":42,"name":"KickStreamer"}]}""",
        };
        (IntegrationOAuthService streamerService, AuthDbContext db, _, _) = Build(streamerHandler);
        Result<OAuthStartDto> streamerStart = await streamerService.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Kick,
            "kick.chat",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        (
            await streamerService.HandleCallbackAsync(
                AuthEnums.IntegrationProvider.Kick,
                new("streamer-auth-code", streamerStart.Value.State, null, null)
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        StubHandler botHandler = new()
        {
            TokenJson =
                """{"access_token":"bot-access","refresh_token":"bot-refresh","expires_in":3600,"scope":"user:read chat:write moderation:ban moderation:chat_message:manage events:subscribe"}""",
            IdentityJson = """{"data":[{"user_id":99,"name":"MyStreamBot"}]}""",
        };
        (IntegrationOAuthService botService, AuthDbContext botDb, _, _) = BuildOnSameDb(
            db,
            botHandler
        );
        Result<OAuthStartDto> botStart = await botService.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.KickBot,
            "kick_bot.chat",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        (
            await botService.HandleCallbackAsync(
                AuthEnums.IntegrationProvider.KickBot,
                new("bot-auth-code", botStart.Value.State, null, null)
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        Result disconnect = await botService.DisconnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.KickBot,
            Actor
        );
        disconnect.IsSuccess.Should().BeTrue(disconnect.ErrorMessage);

        IntegrationConnection botConnection = await botDb
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Provider == AuthEnums.IntegrationProvider.KickBot);
        botConnection.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);

        IntegrationConnection streamerConnection = await botDb
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Provider == AuthEnums.IntegrationProvider.Kick);
        streamerConnection
            .Status.Should()
            .Be(
                AuthEnums.IntegrationStatus.Connected,
                "disconnecting the BOT account must never touch the streamer's own kick connection"
            );
    }

    /// <summary>Disconnecting a provider that isn't Kick never touches the Kick client — no cross-talk.</summary>
    [Fact]
    public async Task Disconnect_ForNonKickProvider_NeverCallsKickApi()
    {
        FakeKickApiClient kick = new([]);
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"spotify-access","refresh_token":"spotify-refresh","expires_in":3600,"scope":"user-read-playback-state"}""",
            IdentityJson = """{"id":"spotify-user-1","display_name":"DJ Test"}""",
        };
        (IntegrationOAuthService service, _, _, _) = Build(
            handler,
            new FakeDiscordGuildService(),
            kick
        );

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new("the-auth-code", start.Value.State, null, null)
        );

        Result disconnect = await service.DisconnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            Actor
        );

        disconnect.IsSuccess.Should().BeTrue(disconnect.ErrorMessage);
        kick.UnsubscribeCalls.Should().BeEmpty();
    }

    // ─── HandleCallback: music provider also mirrored into the legacy Service store ────

    [Fact]
    public async Task HandleCallback_ForSpotify_MirrorsTokensIntoServiceRow_SoMusicProviderFindsThem()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"spotify-access","refresh_token":"spotify-refresh","expires_in":3600,"scope":"user-read-playback-state user-modify-playback-state"}""",
            IdentityJson = """{"id":"spotify-user-1","display_name":"DJ Test"}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new("the-auth-code", start.Value.State, null, null)
        );
        callback.IsSuccess.Should().BeTrue();

        // The bridge wrote the legacy Service row keyed exactly how SpotifyMusicProvider.GetTokenAsync queries it:
        // (BroadcasterId == tenant, Name == "spotify", Enabled, AccessToken != null) — the row whose absence made
        // the provider read the account as disconnected.
        NomNomzBot.Domain.Platform.Entities.Service row = await db
            .Services.AsNoTracking()
            .SingleAsync(s => s.BroadcasterId == Tenant && s.Name == "spotify");
        row.Enabled.Should().BeTrue();
        row.TokenExpiry.Should().NotBeNull();
        row.AccessToken.Should().NotBeNullOrEmpty();
        row.RefreshToken.Should().NotBeNullOrEmpty();
        row.ClientId.Should().NotBeNullOrEmpty();
        row.ClientSecret.Should().NotBeNullOrEmpty();
        // The granted scopes must land in the mirrored Service row too — SpotifyMusicProvider's embedded
        // -playback gate reads Service.Scopes, not the vault, so a mirror that dropped the scope list would
        // leave that gate permanently unsatisfiable no matter how many times the streamer reconnects.
        row.Scopes.Should()
            .BeEquivalentTo("user-read-playback-state", "user-modify-playback-state");

        // The columns are sealed under the SAME TokenProtectionContext the provider unseals them with
        // ((broadcaster, "spotify", field)), so GetTokenAsync would open the vaulted access token and its
        // refresh path would open the app client credentials — the round-trip that makes playback work.
        ITokenProtector reader = AuthTestBuilder.RealTokenProtector(db, out _);
        (
            await reader.TryUnprotectAsync(
                row.AccessToken,
                new(Tenant.ToString(), "spotify", "access")
            )
        )
            .Should()
            .Be("spotify-access");
        (
            await reader.TryUnprotectAsync(
                row.RefreshToken,
                new(Tenant.ToString(), "spotify", "refresh")
            )
        )
            .Should()
            .Be("spotify-refresh");
        (
            await reader.TryUnprotectAsync(
                row.ClientId,
                new(Tenant.ToString(), "spotify", "client_id")
            )
        )
            .Should()
            .Be("spotify-client");
        (
            await reader.TryUnprotectAsync(
                row.ClientSecret,
                new(Tenant.ToString(), "spotify", "client_secret")
            )
        )
            .Should()
            .Be("spotify-secret");
    }

    [Fact]
    public async Task HandleCallback_ForYouTube_VaultsTokens_AndWritesNoLegacyServiceRow()
    {
        // S036c-b — YouTube was migrated OFF the Service-row mirror; the vault write in
        // HandleCallbackAsync (IIntegrationTokenVault.StoreTokensAsync, exercised for every provider) is
        // now the ONLY custody path for a YouTube grant.
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"yt-access","refresh_token":"yt-refresh","expires_in":3600,"scope":"https://www.googleapis.com/auth/youtube"}""",
            IdentityJson = """{"sub":"yt-user-1","name":"YT Test"}""",
        };
        (IntegrationOAuthService service, AuthDbContext db, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.YouTube,
            "youtube.manage",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.YouTube,
            new("the-auth-code", start.Value.State, null, null)
        );
        callback.IsSuccess.Should().BeTrue();

        // No legacy Service row for YouTube — the mirror is Spotify-only now.
        (await db.Services.AnyAsync(s => s.BroadcasterId == Tenant && s.Name == "youtube"))
            .Should()
            .BeFalse("YouTube must no longer write the legacy Service token store");

        // The vault holds the connection + tokens, decryptable back to the exact plaintext exchanged.
        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c =>
                c.BroadcasterId == Tenant && c.Provider == AuthEnums.IntegrationProvider.YouTube
            );
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);

        IntegrationTokenVault vault = new(
            db,
            AuthTestBuilder.RealTokenProtector(db, out ISubjectKeyService keys),
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        Result<DecryptedTokenDto> access = await vault.GetAccessTokenAsync(connection.Id);
        access.IsSuccess.Should().BeTrue();
        access.Value.Value.Should().Be("yt-access");

        Result<DecryptedTokenDto> refresh = await vault.GetRefreshTokenAsync(connection.Id);
        refresh.IsSuccess.Should().BeTrue();
        refresh.Value.Value.Should().Be("yt-refresh");
    }

    [Fact]
    public async Task HandleCallback_ReplayedState_FailsClosed()
    {
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"a","refresh_token":"r","expires_in":3600,"scope":"user-read-playback-state"}""",
            IdentityJson = """{"id":"u","display_name":"n"}""",
        };
        (IntegrationOAuthService service, _, _, _) = Build(handler);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        OAuthCallbackParams cb = new("code", start.Value.State, null, null);
        (await service.HandleCallbackAsync(AuthEnums.IntegrationProvider.Spotify, cb))
            .IsSuccess.Should()
            .BeTrue();

        // The state is single-use — a replay must fail closed.
        Result<OAuthCallbackResultDto> replay = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            cb
        );
        replay.ErrorCode.Should().Be("INVALID_STATE");
    }

    // ─── BYOC (S-BYOC-spotify-a): channel-own Spotify credentials win over the app-level fallback ──

    /// <summary>
    /// Proves the resolution order the OAuth authorize step uses: with a channel-own Spotify client id +
    /// secret stored (sealed at rest, exactly like the system-level rows), the authorize URL carries the
    /// CHANNEL's client id — not the app-level env fallback the sibling test above proves.
    /// </summary>
    [Fact]
    public async Task StartConnect_UsesChannelOwnClientId_WhenBothFieldsAreStored()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        await SeedChannelSpotifyCredentialsAsync(
            db,
            protector,
            Tenant,
            "channel-own-client",
            "channel-own-secret"
        );
        (IntegrationOAuthService service, _, _, _) = BuildWith(db, protector, new());

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            returnUrl: null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.IsSuccess.Should().BeTrue(start.ErrorMessage);
        start.Value.AuthorizeUrl.Should().Contain("client_id=channel-own-client");
        start.Value.AuthorizeUrl.Should().NotContain("client_id=spotify-client");
    }

    /// <summary>
    /// Proves the resolution order the token-exchange step uses: the callback's POST to Spotify's token
    /// endpoint carries the CHANNEL's own client id + secret, not the app-level ones — the exact seam
    /// SpotifyMusicProvider's own refresh later reuses for the same connection.
    /// </summary>
    [Fact]
    public async Task HandleCallback_SendsChannelOwnClientCredentials_InTheTokenExchange()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        await SeedChannelSpotifyCredentialsAsync(
            db,
            protector,
            Tenant,
            "channel-own-client",
            "channel-own-secret"
        );
        StubHandler handler = new()
        {
            TokenJson =
                """{"access_token":"spot-access","refresh_token":"spot-refresh","expires_in":3600,"scope":"user-modify-playback-state"}""",
        };
        (IntegrationOAuthService service, AuthDbContext resultDb, _, _) = BuildWith(
            db,
            protector,
            handler
        );

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            returnUrl: null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );
        start.IsSuccess.Should().BeTrue(start.ErrorMessage);

        Result<OAuthCallbackResultDto> callback = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new("the-auth-code", start.Value.State, null, null)
        );

        callback.IsSuccess.Should().BeTrue(callback.ErrorMessage);
        handler.LastTokenRequestBody.Should().Contain("client_id=channel-own-client");
        handler.LastTokenRequestBody.Should().Contain("client_secret=channel-own-secret");
        handler.LastTokenRequestBody.Should().NotContain("spotify-client");

        // The vaulted connection remembers the channel's own client id (S003 no-Service-row-read
        // contract) — proves the persisted state actually reflects the channel-own credential, not
        // just the one outbound HTTP call.
        IntegrationConnection connection = await resultDb
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.ClientId.Should().Be("channel-own-client");
    }

    /// <summary>Encrypted-at-rest: the channel's sealed Spotify secret column never contains the plaintext,
    /// and the resolver only ever opens it back through the real AAD-bound protector.</summary>
    [Fact]
    public async Task ChannelCredential_SecretIsSealedAtRest_NotPlaintext()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        await SeedChannelSpotifyCredentialsAsync(
            db,
            protector,
            Tenant,
            "channel-own-client",
            "super-secret-value"
        );

        ConfigEntity row = db.Configurations.Single(c => c.Key == "spotify.client_secret");
        row.SecureValue.Should().NotBeNullOrEmpty();
        row.SecureValue.Should().NotContain("super-secret-value");
    }

    /// <summary>With neither a channel-own nor an app-level credential configured, the resolver fails
    /// closed — never a silently-wrong client id.</summary>
    [Fact]
    public async Task StartConnect_Fails_WhenNeitherChannelNorAppLevelCredentialsAreConfigured()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(db, out _);
        IConfiguration emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://api.example.test" }
            )
            .Build();
        (IntegrationOAuthService service, _, _, _) = BuildWith(db, protector, new(), emptyConfig);

        Result<OAuthStartDto> start = await service.StartConnectAsync(
            Tenant,
            AuthEnums.IntegrationProvider.Spotify,
            "spotify.playback",
            returnUrl: null,
            Actor,
            publicOrigin: "https://bot-dev.nomercy.tv"
        );

        start.IsFailure.Should().BeTrue();
        start.ErrorCode.Should().Be("PROVIDER_NOT_CONFIGURED");
    }

    private static async Task SeedChannelSpotifyCredentialsAsync(
        AuthDbContext db,
        ITokenProtector protector,
        Guid channelId,
        string clientId,
        string clientSecret
    )
    {
        db.Configurations.Add(
            new()
            {
                BroadcasterId = channelId,
                Key = "spotify.client_id",
                Value = clientId,
            }
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = channelId,
                Key = "spotify.client_secret",
                SecureValue = await protector.ProtectAsync(
                    clientSecret,
                    NomNomzBot.Infrastructure.Platform.Configuration.ChannelCredentialsResolver.ContextFor(
                        channelId,
                        "spotify"
                    )
                ),
            }
        );
        await db.SaveChangesAsync();
    }

    private static (
        IntegrationOAuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        FakeCache Cache
    ) BuildWith(
        AuthDbContext db,
        ITokenProtector protector,
        StubHandler handler,
        IConfiguration? config = null,
        IKickApiClient? kick = null
    )
    {
        // A fresh protector/key-service pair over the SAME db is functionally interchangeable with the
        // caller's own `protector` instance (both derive the same deterministic KEK and read/write the same
        // persisted CryptoKey rows) — this just needs an ISubjectKeyService for the vault's constructor.
        AuthTestBuilder.RealTokenProtector(db, out ISubjectKeyService keys);
        IIntegrationTokenVault vault = new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        config ??= new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = "https://api.example.test",
                    ["Spotify:ClientId"] = "spotify-client",
                    ["Spotify:ClientSecret"] = "spotify-secret",
                }
            )
            .Build();

        OAuthProviderRegistry registry = new(config);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        IChannelCredentialsResolver channelCredentials = AuthTestBuilder.ChannelCredentialsResolver(
            db,
            protector,
            credentials
        );
        FakeCache cache = new();
        IntegrationOAuthService service = new(
            registry,
            vault,
            new FakeDiscordGuildService(),
            kick ?? new FakeKickApiClient([]),
            new InMemoryIntegrationCapabilityStore(),
            channelCredentials,
            new MusicProviderTokenMirror(
                db,
                protector,
                NullLogger<MusicProviderTokenMirror>.Instance
            ),
            cache,
            db,
            new SingleClientFactory(handler),
            config,
            TimeProvider.System,
            NullLogger<IntegrationOAuthService>.Instance
        );
        return (service, db, vault, cache);
    }

    [Fact]
    public async Task HandleCallback_ProviderError_FailsClosed()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new());

        Result<OAuthCallbackResultDto> result = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new(null, "state", "access_denied", "user said no")
        );

        result.ErrorCode.Should().Be("PROVIDER_ERROR");
    }

    // ─── GetStatus: Discord folded into the unified read model ─────────────────

    [Fact]
    public async Task GetStatus_NoDiscordConnection_ReportsDiscordDisconnected_AlongsideGenericProviders()
    {
        // No Discord connection seeded; the generic providers also unconnected (vault empty).
        (IntegrationOAuthService service, _, _, _) = Build(new(), new FakeDiscordGuildService());

        Result<IReadOnlyList<IntegrationStatusDto>> status = await service.GetStatusAsync(Tenant);

        status.IsSuccess.Should().BeTrue();
        IReadOnlyList<IntegrationStatusDto> rows = status.Value;

        // The one status surface carries every provider: the generic registry set + Discord.
        rows.Select(r => r.Provider)
            .Should()
            .BeEquivalentTo([
                AuthEnums.IntegrationProvider.Spotify,
                AuthEnums.IntegrationProvider.YouTube,
                AuthEnums.IntegrationProvider.Kick,
                AuthEnums.IntegrationProvider.KickBot,
                AuthEnums.IntegrationProvider.Patreon,
                AuthEnums.IntegrationProvider.Shopify,
                AuthEnums.IntegrationProvider.Treatstream,
                AuthEnums.IntegrationProvider.Discord,
            ]);

        IntegrationStatusDto discord = rows.Single(r =>
            r.Provider == AuthEnums.IntegrationProvider.Discord
        );
        discord.Connected.Should().BeFalse();
        discord.AccountName.Should().BeNull();
        discord.NeedsReauth.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_WithDiscordConnection_ReportsDiscordConnectedWithGuildName()
    {
        DiscordGuildConnectionDto link = new(
            Id: Guid.Parse("0192a000-0000-7000-8000-0000000000d1"),
            BroadcasterId: Tenant,
            GuildId: "987654321",
            GuildName: "Test Guild",
            BotInstalled: true,
            ServerConsentStatus: "approved",
            ApprovedByDiscordUserId: "111",
            ApprovedAt: DateTime.UtcNow,
            StreamerEnabled: true,
            IsLinkActive: true,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );
        (IntegrationOAuthService service, _, _, _) = Build(
            new(),
            new FakeDiscordGuildService(link)
        );

        Result<IReadOnlyList<IntegrationStatusDto>> status = await service.GetStatusAsync(Tenant);

        status.IsSuccess.Should().BeTrue();
        IntegrationStatusDto discord = status.Value.Single(r =>
            r.Provider == AuthEnums.IntegrationProvider.Discord
        );
        discord.Connected.Should().BeTrue();
        discord.AccountName.Should().Be("Test Guild");

        // Folding Discord in does not drop the generic providers from the same surface.
        status
            .Value.Select(r => r.Provider)
            .Should()
            .Contain([
                AuthEnums.IntegrationProvider.Spotify,
                AuthEnums.IntegrationProvider.YouTube,
            ]);
    }

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static (
        IntegrationOAuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        FakeCache Cache
    ) Build(StubHandler handler) => Build(handler, new FakeDiscordGuildService());

    private static (
        IntegrationOAuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        FakeCache Cache
    ) Build(StubHandler handler, IDiscordGuildService discord, IKickApiClient? kick = null)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IIntegrationTokenVault vault = new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = "https://api.example.test",
                    ["Spotify:ClientId"] = "spotify-client",
                    ["Spotify:ClientSecret"] = "spotify-secret",
                    ["YouTube:ClientId"] = "youtube-client",
                    ["YouTube:ClientSecret"] = "youtube-secret",
                    ["Patreon:ClientId"] = "patreon-client",
                    ["Patreon:ClientSecret"] = "patreon-secret",
                    ["Shopify:ClientId"] = "shopify-client",
                    ["Shopify:ClientSecret"] = "shopify-secret",
                    ["Treatstream:ClientId"] = "ts-client",
                    ["Treatstream:ClientSecret"] = "ts-secret",
                    ["Kick:ClientId"] = "kick-client",
                    ["Kick:ClientSecret"] = "kick-secret",
                }
            )
            .Build();

        OAuthProviderRegistry registry = new(config);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        IChannelCredentialsResolver channelCredentials = AuthTestBuilder.ChannelCredentialsResolver(
            db,
            protector,
            credentials
        );
        FakeCache cache = new();
        IntegrationOAuthService service = new(
            registry,
            vault,
            discord,
            kick ?? new FakeKickApiClient([]),
            new InMemoryIntegrationCapabilityStore(),
            channelCredentials,
            new MusicProviderTokenMirror(
                db,
                protector,
                NullLogger<MusicProviderTokenMirror>.Instance
            ),
            cache,
            db,
            new SingleClientFactory(handler),
            config,
            TimeProvider.System,
            NullLogger<IntegrationOAuthService>.Instance
        );
        return (service, db, vault, cache);
    }

    /// <summary>
    /// A second service instance over the SAME db + config as an earlier <see cref="Build(StubHandler)"/> call
    /// (a fresh protector/key-service pair over that db is functionally interchangeable — see the analogous
    /// note on <c>BuildWith</c>), but with its own HTTP handler — needed to prove a bot-account connect lands
    /// alongside an already-persisted streamer connection in the SAME tenant's row set, not a separate db.
    /// </summary>
    private static (
        IntegrationOAuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        FakeCache Cache
    ) BuildOnSameDb(AuthDbContext db, StubHandler handler)
    {
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IIntegrationTokenVault vault = new IntegrationTokenVault(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = "https://api.example.test",
                    ["Kick:ClientId"] = "kick-client",
                    ["Kick:ClientSecret"] = "kick-secret",
                }
            )
            .Build();

        OAuthProviderRegistry registry = new(config);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        IChannelCredentialsResolver channelCredentials = AuthTestBuilder.ChannelCredentialsResolver(
            db,
            protector,
            credentials
        );
        FakeCache cache = new();
        IntegrationOAuthService service = new(
            registry,
            vault,
            new FakeDiscordGuildService(),
            new FakeKickApiClient([]),
            new InMemoryIntegrationCapabilityStore(),
            channelCredentials,
            new MusicProviderTokenMirror(
                db,
                protector,
                NullLogger<MusicProviderTokenMirror>.Instance
            ),
            cache,
            db,
            new SingleClientFactory(handler),
            config,
            TimeProvider.System,
            NullLogger<IntegrationOAuthService>.Instance
        );
        return (service, db, vault, cache);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Spotify:ClientId"] = "c" })
            .Build();

    /// <summary>A canned HTTP handler: the token endpoint returns <see cref="TokenJson"/>, "me" returns <see cref="IdentityJson"/>.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string TokenJson { get; init; } =
            """{"access_token":"a","refresh_token":"r","expires_in":3600,"scope":"user-read-playback-state"}""";
        public string IdentityJson { get; init; } = """{"id":"u","display_name":"n"}""";
        public string? LastTokenRequestBody { get; private set; }
        public Uri? LastTokenRequestUri { get; private set; }
        public Uri? LastIdentityRequestUri { get; private set; }
        public string? LastIdentityShopifyHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isToken = request.RequestUri!.AbsoluteUri.Contains("token");
            if (isToken)
            {
                LastTokenRequestUri = request.RequestUri;
                if (request.Content is not null)
                    LastTokenRequestBody = await request.Content.ReadAsStringAsync(
                        cancellationToken
                    );
            }
            else
            {
                LastIdentityRequestUri = request.RequestUri;
                LastIdentityShopifyHeader = request.Headers.TryGetValues(
                    "X-Shopify-Access-Token",
                    out IEnumerable<string>? values
                )
                    ? values.FirstOrDefault()
                    : null;
            }

            string body = isToken ? TokenJson : IdentityJson;
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeCache : ICacheService
    {
        private readonly ConcurrentDictionary<string, object?> _store = new();

        public bool Contains(string key) => _store.ContainsKey(key);

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? v) ? (T?)v : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }

    private sealed class PassthroughScopeGrant : IScopeGrantService
    {
        public IReadOnlyList<string> RequiredScopesFor(string featureKey) => [];

        public Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
            Guid broadcasterId,
            string featureKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScopeGrantState(true, null, [])));

        public Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
            Guid connectionId,
            IReadOnlyList<string> actualScopes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));
    }

    /// <summary>
    /// A Discord guild service double at the <see cref="IDiscordGuildService"/> seam: it returns exactly the
    /// connection list it is seeded with, so a status test can prove how <c>GetStatusAsync</c> folds Discord into
    /// the unified read model (the real DB→DTO mapping is covered by <c>DiscordGuildServiceTests</c>). Only the
    /// read path the status surface uses is implemented.
    /// </summary>
    private sealed class FakeDiscordGuildService : IDiscordGuildService
    {
        private readonly IReadOnlyList<DiscordGuildConnectionDto> _connections;

        public FakeDiscordGuildService(params DiscordGuildConnectionDto[] connections) =>
            _connections = connections;

        public Task<Result<IReadOnlyList<DiscordGuildConnectionDto>>> GetConnectionsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(_connections));

        public Task<Result<DiscordGuildConnectionDto>> GetConnectionAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<DiscordGuildConnectionDto>> UpsertFromOAuthAsync(
            Guid broadcasterId,
            DiscordGuildOAuthResult oauth,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> ApproveServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            string approvedByDiscordUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> RevokeServerConsentAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> SetStreamerEnabledAsync(
            Guid broadcasterId,
            Guid connectionId,
            bool enabled,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<BlastRadiusDto>> GetDisconnectBlastRadiusAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> DisconnectAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<bool>> IsLinkActiveAsync(
            Guid broadcasterId,
            Guid connectionId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    /// <summary>
    /// A Kick API double: seeded with the subscriptions Kick would currently report, and records every
    /// unsubscribe call so a disconnect test can prove the real subscription ids were sent, not just that
    /// SOME call happened.
    /// </summary>
    private sealed class FakeKickApiClient(IReadOnlyList<KickEventSubscription> subscriptions)
        : IKickApiClient
    {
        public List<IReadOnlyList<string>> UnsubscribeCalls { get; } = [];

        public Task<Result<string>> SendMessageAsync(
            string accessToken,
            long broadcasterUserId,
            string content,
            string? replyToMessageId = null,
            bool isBotAccount = false,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> DeleteMessageAsync(
            string accessToken,
            string messageId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> TimeoutUserAsync(
            string accessToken,
            long broadcasterUserId,
            long userId,
            int durationMinutes,
            string? reason = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> BanUserAsync(
            string accessToken,
            long broadcasterUserId,
            long userId,
            string? reason = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> UnbanUserAsync(
            string accessToken,
            long broadcasterUserId,
            long userId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<KickEventSubscription>>> ListEventSubscriptionsAsync(
            string accessToken,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(subscriptions));

        public Task<Result> SubscribeAsync(
            string accessToken,
            IReadOnlyList<KickEventRequest> events,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> UnsubscribeAsync(
            string accessToken,
            IReadOnlyList<string> subscriptionIds,
            CancellationToken cancellationToken = default
        )
        {
            UnsubscribeCalls.Add(subscriptionIds);
            return Task.FromResult(Result.Success());
        }

        public Task<Result<KickChannel>> GetChannelAsync(
            string accessToken,
            long broadcasterUserId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<Result> UpdateChannelAsync(
            string accessToken,
            string? streamTitle,
            int? categoryId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
