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
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Integrations.YouTube;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Round-trips plaintext unchanged — provider-seam tests exercise the music plumbing,
/// not the envelope-encryption stack, which has its own dedicated tests elsewhere.</summary>
internal sealed class PassthroughProtector : ITokenProtector
{
    public Task<string> ProtectAsync(
        string plaintext,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(plaintext);

    public Task<string?> TryUnprotectAsync(
        string? sealedEnvelope,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(sealedEnvelope);
}

/// <summary>Hands every named client the one test handler.</summary>
internal sealed class SingleHandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// S003 — a minimal, real-shaped <see cref="IIntegrationTokenVault"/> double for
/// <see cref="SpotifyMusicProvider"/> tests: plaintext in-memory token storage keyed by connection id
/// (no crypto — the envelope-encryption stack has its own dedicated tests), but the SAME status/failure
/// semantics as the production vault (<c>StoreTokensAsync</c> resets the connection back to
/// <c>connected</c>; a connection's <c>Status</c> lives on the real <see cref="IntegrationConnection"/>
/// row so <c>SpotifyMusicProvider</c>'s own connection lookup — <c>Status != "revoked"</c> — behaves
/// identically to production). Only the members <c>SpotifyMusicProvider</c> actually calls are
/// meaningfully implemented; the rest of the interface throws — a call there would mean the provider
/// started depending on a vault member this test double was never asked to support.
/// </summary>
internal sealed class FakeIntegrationTokenVault : IIntegrationTokenVault
{
    // Keyed by connection id, but the backing dictionary itself is scoped PER DB INSTANCE (not
    // per-FakeIntegrationTokenVault-instance, and not process-global either): several production call
    // sites build a NEW SpotifyMusicProvider/MusicService per simulated DI scope over the SAME db —
    // exactly like the real container handing out scoped instances per request — and each of those
    // scopes constructs its OWN FakeIntegrationTokenVault. A purely per-instance dictionary would make
    // a token "vaulted" in scope 1 invisible to scope 2 even though they share the same db, defeating
    // the cross-scope tests this double exists to support (S001/S003). A single process-global
    // dictionary (the prior shape) went further than that and let UNRELATED test classes — different
    // dbs entirely — see and corrupt each other's tokens under parallel xUnit execution. Routing
    // through a ConditionalWeakTable keyed by the db instance gets both: same-db scopes still share
    // state, different-db test classes never do, and entries are GC'd with their db. The table + its
    // per-db ConcurrentDictionary are both thread-safe, covering same-scope concurrent access too.
    private static readonly ConditionalWeakTable<
        IApplicationDbContext,
        ConcurrentDictionary<Guid, (string Access, string? Refresh, DateTime? ExpiresAt)>
    > _tokensByDb = [];

    private readonly IApplicationDbContext _db;

    private ConcurrentDictionary<
        Guid,
        (string Access, string? Refresh, DateTime? ExpiresAt)
    > _tokens => _tokensByDb.GetValue(_db, static _ => new());

    public FakeIntegrationTokenVault(IApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>Seeds a usable, non-expiring Spotify connection + token pair for a broadcaster, exactly
    /// as a real OAuth connect would leave the vault. Returns the connection id for tests that need to
    /// mutate it directly (e.g. to simulate an expired/un-refreshable token).</summary>
    public Guid SeedConnectedSpotify(
        Guid broadcasterId,
        string accessToken = "test-access-token",
        string? refreshToken = null,
        IReadOnlyList<string>? scopes = null,
        DateTime? expiresAt = null
    )
    {
        IntegrationConnection connection = new()
        {
            BroadcasterId = broadcasterId,
            Provider = AuthEnums.IntegrationProvider.Spotify,
            Status = AuthEnums.IntegrationStatus.Connected,
            Scopes = scopes is null ? [] : [.. scopes],
        };
        _db.IntegrationConnections.Add(connection);
        _db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

        _tokens[connection.Id] = (accessToken, refreshToken, expiresAt);
        return connection.Id;
    }

    /// <summary>Seeds a usable, non-expiring YouTube connection + token pair for a broadcaster (S036c-b),
    /// exactly as a real OAuth connect (or the S036c-a backfill) would leave the vault. Returns the
    /// connection id for tests that need to mutate it directly.</summary>
    public Guid SeedConnectedYouTube(
        Guid broadcasterId,
        string accessToken = "test-access-token",
        string? refreshToken = null,
        IReadOnlyList<string>? scopes = null,
        DateTime? expiresAt = null
    )
    {
        IntegrationConnection connection = new()
        {
            BroadcasterId = broadcasterId,
            Provider = AuthEnums.IntegrationProvider.YouTube,
            Status = AuthEnums.IntegrationStatus.Connected,
            Scopes = scopes is null ? [] : [.. scopes],
        };
        _db.IntegrationConnections.Add(connection);
        _db.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

        _tokens[connection.Id] = (accessToken, refreshToken, expiresAt);
        return connection.Id;
    }

    /// <summary>Marks a previously-seeded connection's token dead: expired with no refresh token on
    /// file — the vault equivalent of "GetTokenAsync resolves to null for every Spotify call".</summary>
    public void MakeUnrefreshable(Guid connectionId)
    {
        (string Access, string? Refresh, DateTime? ExpiresAt) current = _tokens[connectionId];
        _tokens[connectionId] = (current.Access, null, DateTime.UtcNow.AddDays(-1));
    }

    public Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_tokens.TryGetValue(connectionId, out var entry))
            return Task.FromResult(
                Result.Failure<DecryptedTokenDto>("No such token.", "NOT_FOUND")
            );

        bool isExpired = entry.ExpiresAt is { } expiresAt && expiresAt <= DateTime.UtcNow;
        return Task.FromResult(
            Result.Success(
                new DecryptedTokenDto(
                    entry.Access,
                    AuthEnums.TokenType.Access,
                    entry.ExpiresAt,
                    isExpired
                )
            )
        );
    }

    public Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_tokens.TryGetValue(connectionId, out var entry) || entry.Refresh is null)
            return Task.FromResult(
                Result.Failure<DecryptedTokenDto>("No refresh token on file.", "NOT_FOUND")
            );

        return Task.FromResult(
            Result.Success(
                new DecryptedTokenDto(entry.Refresh, AuthEnums.TokenType.Refresh, null, false)
            )
        );
    }

    public async Task<Result> StoreTokensAsync(
        Guid connectionId,
        StoreTokensDto tokens,
        IReadOnlyList<string>? grantedScopes = null,
        CancellationToken cancellationToken = default
    )
    {
        _tokens.TryGetValue(connectionId, out var existing);
        _tokens[connectionId] = (
            tokens.AccessToken,
            tokens.RefreshToken ?? existing.Refresh,
            tokens.AccessExpiresAt
        );

        IntegrationConnection? connection = await _db.IntegrationConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId,
            cancellationToken
        );
        if (connection is not null)
        {
            connection.Status = AuthEnums.IntegrationStatus.Connected;
            if (grantedScopes is not null)
                connection.Scopes = [.. grantedScopes];
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result> MarkRefreshFailureAsync(
        Guid connectionId,
        string error,
        CancellationToken cancellationToken = default
    )
    {
        IntegrationConnection? connection = await _db.IntegrationConnections.FirstOrDefaultAsync(
            c => c.Id == connectionId,
            cancellationToken
        );
        if (connection is null)
            return Result.Failure("No such connection.", "NOT_FOUND");

        connection.Status = AuthEnums.IntegrationStatus.NeedsReauth;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> RevokeConnectionAsync(
        Guid connectionId,
        string reason,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Not exercised by the SpotifyMusicProvider test surface.");

    public Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(
        UpsertConnectionDto request,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Not exercised by the SpotifyMusicProvider test surface.");

    public Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Not exercised by the SpotifyMusicProvider test surface.");
}

/// <summary>Never configured — <c>SpotifyMusicProvider</c> only reaches this when refreshing, and no
/// test here exercises a refresh (tokens seeded via <see cref="FakeIntegrationTokenVault"/> never expire
/// unless a test explicitly calls <c>MakeUnrefreshable</c>, which removes the refresh token too — so
/// <c>RefreshTokenAsync</c> always short-circuits on the vault's own <c>GetRefreshTokenAsync</c> failure
/// before this would ever be consulted).</summary>
internal sealed class NullSystemCredentialsProvider : ISystemCredentialsProvider
{
    public static readonly NullSystemCredentialsProvider Instance = new();

    public Task<SystemAppCredentials?> GetAsync(
        string provider,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<SystemAppCredentials?>(null);

    public Task<string?> GetClientIdAsync(
        string provider,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<string?>(null);

    public Task<string?> GetValueAsync(
        string provider,
        string field,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<string?>(null);

    public Task<bool> IsAppDecisionRecordedAsync(
        string provider,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(false);
}

/// <summary>Layers over <see cref="NullSystemCredentialsProvider"/> the way the real
/// <c>ChannelCredentialsResolver</c> layers over <c>ISystemCredentialsProvider</c>: no channel ever has its
/// own stored credentials here, so resolution always falls through to the app-level provider and fails with
/// <c>PROVIDER_NOT_CONFIGURED</c> when that is null too — these tests aren't exercising BYOC, they need a
/// resolver dependency that behaves exactly like "nothing configured" without a real DB round-trip.</summary>
internal sealed class NullChannelCredentialsResolver(ISystemCredentialsProvider systemCredentials)
    : IChannelCredentialsResolver
{
    public async Task<Result<SystemAppCredentials>> ResolveAsync(
        Guid channelId,
        string provider,
        CancellationToken cancellationToken = default
    )
    {
        SystemAppCredentials? credentials = await systemCredentials.GetAsync(
            provider,
            cancellationToken
        );
        return credentials is not null
            ? Result.Success(credentials)
            : Result.Failure<SystemAppCredentials>("PROVIDER_NOT_CONFIGURED");
    }
}

/// <summary>
/// Records every request a music provider sends (method + absolute URL, plus the JSON body when
/// present) and answers from registered routes; anything unrouted gets a 404 so a test can prove an
/// endpoint was NOT called with real consequences instead of silence.
/// </summary>
internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Matches,
        HttpStatusCode Status,
        string? Json
    )> _routes = [];

    public List<string> RequestUrls { get; } = [];

    /// <summary>Body per recorded request, index-aligned with <see cref="RequestUrls"/> ("" when none).</summary>
    public List<string> RequestBodies { get; } = [];

    public void RespondWhen(
        Func<HttpRequestMessage, bool> matches,
        HttpStatusCode status,
        string? json = null
    ) => _routes.Add((matches, status, json));

    /// <summary>Drops every previously-registered route (requests already recorded are kept) — for a test
    /// that simulates a connection RECOVERING mid-test (e.g. a later call succeeding after an earlier one
    /// was rejected), since the first-match-wins dispatch below would otherwise keep answering with the
    /// stale route forever.</summary>
    public void ClearRoutes() => _routes.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestUrls.Add($"{request.Method} {request.RequestUri}");
        RequestBodies.Add(
            request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken)
        );

        foreach (
            (Func<HttpRequestMessage, bool> matches, HttpStatusCode status, string? json) in _routes
        )
        {
            if (!matches(request))
                continue;

            HttpResponseMessage response = new(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }

        return new(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Builds a real <see cref="YouTubeMusicProvider"/> over the shared test HTTP handler, an in-memory
/// <c>YouTube:ApiKey</c>, and a vault-backing <see cref="IApplicationDbContext"/> — mirrors the runtime
/// DI shape (named HttpClient + IConfiguration + db + vault). A null <paramref name="apiKey"/> leaves the
/// provider unconfigured (search/resolve degrade to empty/null); pass a <paramref name="vault"/> with a
/// <see cref="FakeIntegrationTokenVault.SeedConnectedYouTube"/> connection for <paramref name="db"/> to
/// exercise the §3.10 manage surface (else an unconnected db = the MISSING_SCOPE path — S036c-b, the
/// real custody path is <c>IIntegrationTokenVault</c>, not the legacy <c>Service</c> row).
/// </summary>
internal static class YouTubeProviderFactory
{
    public static YouTubeMusicProvider Create(
        string? apiKey = null,
        HttpMessageHandler? handler = null,
        IApplicationDbContext? db = null,
        FakeIntegrationTokenVault? vault = null
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:ApiKey"] = apiKey })
            .Build();

        SingleHandlerClientFactory factory = new(handler ?? new RecordingHttpHandler());

        IApplicationDbContext database =
            db
            ?? new MusicTestDbContext(
                new DbContextOptionsBuilder<MusicTestDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options
            );

        // The REAL shared custody path over the same db/handler — manage-surface tests keep proving the
        // vault-lookup + refresh behavior end to end, now through the extracted provider (S036c-b).
        YouTubeAccessTokenProvider accessTokens = new(
            database,
            vault ?? new FakeIntegrationTokenVault(database),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            TimeProvider.System,
            factory,
            NullLogger<YouTubeAccessTokenProvider>.Instance,
            new NomNomzBot.Infrastructure.Identity.ConnectionRefreshGate()
        );

        return new(factory, configuration, accessTokens, NullLogger<YouTubeMusicProvider>.Instance);
    }
}
