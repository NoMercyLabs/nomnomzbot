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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.YouTube;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Music;

namespace NomNomzBot.Infrastructure.Tests.Integrations.YouTube;

/// <summary>
/// S036c-c — <see cref="YouTubeAccessTokenProvider"/>'s vaulted refresh already acquires
/// <see cref="ConnectionRefreshGate"/> (mirroring the Spotify shape closed by 005273c7), but that shape was
/// never proven under a real concurrent race for YouTube specifically. Proven over the REAL
/// <see cref="IntegrationTokenVault"/> with two INDEPENDENT DbContext instances sharing one SQLite store
/// (the same shape as two concurrent requests each getting their own scoped DbContext) and one shared
/// <see cref="ConnectionRefreshGate"/> — mirrors
/// <c>SpotifyMusicProviderRefreshConcurrencyTests.TwoConcurrentPlayCalls_ForTheSameConnection_HitSpotifyExactlyOnce_AndBothGetTheSameToken</c>.
/// </summary>
public sealed class YouTubeAccessTokenProviderRefreshConcurrencyTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000e1");
    private const string ExternalId = "youtube-112233";

    [Fact]
    public async Task TwoConcurrentGetAccessTokenCalls_ForTheSameConnection_HitGoogleExactlyOnce_AndBothGetTheSameToken()
    {
        string dbName = Guid.NewGuid().ToString();
        ConnectionRefreshGate gate = new();
        CountingGoogleHandler wire = new() { HoldFirstCall = true };

        YouTubeAccessTokenProvider providerA = await BuildAsync(dbName, gate, wire);
        YouTubeAccessTokenProvider providerB = await BuildAsync(dbName, gate, wire);

        // Force a REAL race: t1 is made to block INSIDE the HTTP call (holding the gate), so t2 is
        // guaranteed to arrive while t1's refresh is still in flight and block on the SAME gate key.
        Task<string?> t1 = providerA.GetAccessTokenAsync(Broadcaster);
        await WaitUntilAsync(() => wire.CallCount >= 1); // t1 is now blocked inside the HTTP call
        Task<string?> t2 = providerB.GetAccessTokenAsync(Broadcaster);
        await Task.Delay(50); // give t2 time to reach the vault check + block on the gate
        wire.Release();

        string?[] tokens = await Task.WhenAll(t1, t2);

        wire.CallCount.Should().Be(1, "the second caller must reuse the winner's re-vaulted token");
        tokens[0].Should().Be("yt-access-1");
        tokens[1].Should().Be("yt-access-1");

        // The vault holds exactly one updated record for this connection — no stale/duplicate write race.
        AuthDbContext verifyDb = AuthTestBuilder.NewContext(dbName);
        List<IntegrationConnection> connections = await verifyDb
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.YouTube
                && c.ProviderAccountId == ExternalId
            )
            .ToListAsync();
        connections.Should().HaveCount(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    private static async Task<YouTubeAccessTokenProvider> BuildAsync(
        string dbName,
        ConnectionRefreshGate gate,
        CountingGoogleHandler wire
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(dbName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        if (!db.Channels.Any(c => c.Id == Broadcaster))
        {
            db.Channels.Add(
                new()
                {
                    Id = Broadcaster,
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = "7654321",
                    Name = "youtube-streamer",
                    NameNormalized = "youtube-streamer",
                    OwnerUserId = Guid.NewGuid(),
                }
            );
        }
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        if (
            !await db.IntegrationConnections.AnyAsync(c =>
                c.Provider == AuthEnums.IntegrationProvider.YouTube
                && c.ProviderAccountId == ExternalId
            )
        )
        {
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.YouTube,
                    ProviderAccountId: ExternalId,
                    ProviderAccountName: "youtube-streamer",
                    Scopes: ["https://www.googleapis.com/auth/youtube.readonly"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            // Already expired — the first call must refresh.
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new("old-access", "old-refresh", null, DateTime.UtcNow.AddMinutes(-10))
            );
        }

        return new(
            db,
            vault,
            new NullChannelCredentialsResolver(new FixedYouTubeCredentialsProvider()),
            TimeProvider.System,
            new SingleClientFactory(wire),
            NullLogger<YouTubeAccessTokenProvider>.Instance,
            gate
        );
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

    private sealed class FixedYouTubeCredentialsProvider : ISystemCredentialsProvider
    {
        public Task<SystemAppCredentials?> GetAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<SystemAppCredentials?>(new("youtube-app-id", "youtube-app-secret"));

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>("youtube-app-id");

        public Task<string?> GetValueAsync(
            string provider,
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<bool> IsAppDecisionRecordedAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }

    /// <summary>
    /// Counts every POST to the Google token endpoint. When <see cref="HoldFirstCall"/> is set, the FIRST
    /// token-refresh POST blocks until <see cref="Release"/> is called — forces a REAL race instead of two
    /// calls that happen to run one after the other (which an all-synchronous in-memory test can otherwise
    /// produce, defeating the point of a concurrency test).
    /// </summary>
    private sealed class CountingGoogleHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _callCount;

        public bool HoldFirstCall { get; set; }
        public int CallCount => _callCount;

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            int callNumber = Interlocked.Increment(ref _callCount);
            if (HoldFirstCall && callNumber == 1)
                await _gate.Task.WaitAsync(cancellationToken);

            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"yt-access-{{callNumber}}","expires_in":3600}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
