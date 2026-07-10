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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
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

    // ─── StartConnect: descriptor drives the URL ───────────────────────────────

    [Fact]
    public async Task StartConnect_BuildsAuthorizeUrl_WithScopeSetStateAndPkce()
    {
        (IntegrationOAuthService service, _, _, FakeCache cache) = Build(new StubHandler());

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
        (IntegrationOAuthService service, _, _, _) = Build(new StubHandler());

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
            new OAuthCallbackParams("the-auth-code", start.Value.State, null, null)
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
            new OAuthCallbackParams("the-auth-code", start.Value.State, null, null)
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

        // The columns are sealed under the SAME TokenProtectionContext the provider unseals them with
        // ((broadcaster, "spotify", field)), so GetTokenAsync would open the vaulted access token and its
        // refresh path would open the app client credentials — the round-trip that makes playback work.
        ITokenProtector reader = AuthTestBuilder.RealTokenProtector(db, out _);
        (
            await reader.TryUnprotectAsync(
                row.AccessToken,
                new TokenProtectionContext(Tenant.ToString(), "spotify", "access")
            )
        )
            .Should()
            .Be("spotify-access");
        (
            await reader.TryUnprotectAsync(
                row.RefreshToken,
                new TokenProtectionContext(Tenant.ToString(), "spotify", "refresh")
            )
        )
            .Should()
            .Be("spotify-refresh");
        (
            await reader.TryUnprotectAsync(
                row.ClientId,
                new TokenProtectionContext(Tenant.ToString(), "spotify", "client_id")
            )
        )
            .Should()
            .Be("spotify-client");
        (
            await reader.TryUnprotectAsync(
                row.ClientSecret,
                new TokenProtectionContext(Tenant.ToString(), "spotify", "client_secret")
            )
        )
            .Should()
            .Be("spotify-secret");
    }

    [Fact]
    public async Task HandleCallback_ForYouTube_MirrorsTokensIntoServiceRow()
    {
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
            new OAuthCallbackParams("the-auth-code", start.Value.State, null, null)
        );
        callback.IsSuccess.Should().BeTrue();

        NomNomzBot.Domain.Platform.Entities.Service row = await db
            .Services.AsNoTracking()
            .SingleAsync(s => s.BroadcasterId == Tenant && s.Name == "youtube");
        row.Enabled.Should().BeTrue();
        row.AccessToken.Should().NotBeNullOrEmpty();

        ITokenProtector reader = AuthTestBuilder.RealTokenProtector(db, out _);
        (
            await reader.TryUnprotectAsync(
                row.AccessToken,
                new TokenProtectionContext(Tenant.ToString(), "youtube", "access")
            )
        )
            .Should()
            .Be("yt-access");
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

    [Fact]
    public async Task HandleCallback_ProviderError_FailsClosed()
    {
        (IntegrationOAuthService service, _, _, _) = Build(new StubHandler());

        Result<OAuthCallbackResultDto> result = await service.HandleCallbackAsync(
            AuthEnums.IntegrationProvider.Spotify,
            new OAuthCallbackParams(null, "state", "access_denied", "user said no")
        );

        result.ErrorCode.Should().Be("PROVIDER_ERROR");
    }

    // ─── GetStatus: Discord folded into the unified read model ─────────────────

    [Fact]
    public async Task GetStatus_NoDiscordConnection_ReportsDiscordDisconnected_AlongsideGenericProviders()
    {
        // No Discord connection seeded; Spotify/YouTube also unconnected (vault empty).
        (IntegrationOAuthService service, _, _, _) = Build(
            new StubHandler(),
            new FakeDiscordGuildService()
        );

        Result<IReadOnlyList<IntegrationStatusDto>> status = await service.GetStatusAsync(Tenant);

        status.IsSuccess.Should().BeTrue();
        IReadOnlyList<IntegrationStatusDto> rows = status.Value;

        // The one status surface carries every provider: the generic registry pair + Discord.
        rows.Select(r => r.Provider)
            .Should()
            .BeEquivalentTo([
                AuthEnums.IntegrationProvider.Spotify,
                AuthEnums.IntegrationProvider.YouTube,
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
            new StubHandler(),
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
    ) Build(StubHandler handler, IDiscordGuildService discord)
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
                }
            )
            .Build();

        OAuthProviderRegistry registry = new(config);
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        FakeCache cache = new();
        IntegrationOAuthService service = new(
            registry,
            vault,
            discord,
            new InMemoryIntegrationCapabilityStore(),
            credentials,
            new MusicProviderTokenMirror(
                db,
                protector,
                NullLogger<MusicProviderTokenMirror>.Instance
            ),
            cache,
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

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isToken = request.RequestUri!.AbsoluteUri.Contains("token");
            if (isToken && request.Content is not null)
                LastTokenRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            string body = isToken ? TokenJson : IdentityJson;
            return new HttpResponseMessage(HttpStatusCode.OK)
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
}
