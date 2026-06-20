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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;

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
            Actor
        );

        start.IsSuccess.Should().BeTrue();
        string url = start.Value.AuthorizeUrl;
        url.Should().StartWith("https://accounts.spotify.com/authorize");
        url.Should().Contain("client_id=spotify-client");
        url.Should().Contain("response_type=code");
        url.Should().Contain("code_challenge_method=S256");
        url.Should().Contain($"state={start.Value.State}");
        Uri.UnescapeDataString(url).Should().Contain("user-modify-playback-state");

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
            Actor
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
            Actor
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
            Actor
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

    // ─── scaffolding ───────────────────────────────────────────────────────────

    private static (
        IntegrationOAuthService Service,
        AuthDbContext Db,
        IIntegrationTokenVault Vault,
        FakeCache Cache
    ) Build(StubHandler handler)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(out ISubjectKeyService keys);
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
                }
            )
            .Build();

        OAuthProviderRegistry registry = new(config);
        FakeCache cache = new();
        IntegrationOAuthService service = new(
            registry,
            vault,
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
}
