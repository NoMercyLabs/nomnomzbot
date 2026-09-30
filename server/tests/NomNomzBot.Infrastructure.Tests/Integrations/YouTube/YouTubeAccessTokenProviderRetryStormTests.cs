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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.YouTube;
using NomNomzBot.Infrastructure.Tests.Gdpr;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Music;

namespace NomNomzBot.Infrastructure.Tests.Integrations.YouTube;

/// <summary>
/// A YouTube connection whose refresh token Google has revoked must not be POSTed to Google on every
/// tick: once needs_reauth, the routine refresh makes no call at all, and short of that a failure backs
/// off instead of retrying at full cadence — the same guard Kick and Spotify carry.
/// </summary>
public sealed class YouTubeAccessTokenProviderRetryStormTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000e3");

    [Fact]
    public async Task A_needs_reauth_connection_makes_zero_refresh_calls()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new();
        Harness harness = await Harness.BuildAsync(wire, clock);
        for (int i = 0; i < 3; i++)
            await harness.Vault.MarkRefreshFailureAsync(harness.ConnectionId, "invalid_grant");

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(15));
            (await harness.Provider.GetAccessTokenAsync(Broadcaster)).Should().BeNull();
        }

        wire.CallCount.Should().Be(0, "only a fresh OAuth grant can clear needs_reauth");
    }

    [Fact]
    public async Task A_failed_refresh_backs_off_before_the_next_attempt()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new() { FailWith = HttpStatusCode.ServiceUnavailable };
        Harness harness = await Harness.BuildAsync(wire, clock);

        await harness.Provider.GetAccessTokenAsync(Broadcaster);
        clock.Advance(TimeSpan.FromSeconds(5));
        await harness.Provider.GetAccessTokenAsync(Broadcaster);
        wire.CallCount.Should().Be(1, "the second tick is inside the backoff window");

        clock.Advance(TimeSpan.FromMinutes(1));
        await harness.Provider.GetAccessTokenAsync(Broadcaster);
        wire.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Invalid_grant_three_times_marks_needs_reauth_and_stops_the_calls()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new() { FailWith = HttpStatusCode.BadRequest };
        Harness harness = await Harness.BuildAsync(wire, clock);

        for (int i = 0; i < 5; i++)
        {
            await harness.Provider.GetAccessTokenAsync(Broadcaster);
            clock.Advance(TimeSpan.FromMinutes(11));
        }

        wire.CallCount.Should().Be(3);
        IntegrationConnection connection = await harness
            .Db.IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Id == harness.ConnectionId);
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
    }

    private sealed class Harness
    {
        public required YouTubeAccessTokenProvider Provider { get; init; }
        public required IntegrationTokenVault Vault { get; init; }
        public required AuthDbContext Db { get; init; }
        public required Guid ConnectionId { get; init; }

        public static async Task<Harness> BuildAsync(
            CountingGoogleHandler wire,
            FakeTimeProvider clock
        )
        {
            AuthDbContext db = AuthTestBuilder.NewContext();
            ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
                db,
                out ISubjectKeyService keys
            );
            IntegrationTokenVault vault = new(
                db,
                protector,
                keys,
                new NoopScopeGrantService(),
                new RecordingEventBus(),
                clock,
                NullLogger<IntegrationTokenVault>.Instance
            );
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.YouTube,
                    ProviderAccountId: "youtube-storm",
                    ProviderAccountName: "storm-streamer",
                    Scopes: ["https://www.googleapis.com/auth/youtube.readonly"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new(
                    "expired-access",
                    "revoked-refresh",
                    null,
                    clock.GetUtcNow().UtcDateTime.AddMinutes(-30)
                )
            );

            YouTubeAccessTokenProvider provider = new(
                db,
                vault,
                new NullChannelCredentialsResolver(new FixedYouTubeCredentialsProvider()),
                clock,
                new SingleClientFactory(wire),
                NullLogger<YouTubeAccessTokenProvider>.Instance,
                new ConnectionRefreshGate()
            );
            return new()
            {
                Provider = provider,
                Vault = vault,
                Db = db,
                ConnectionId = upsert.Value.Id,
            };
        }
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

    /// <summary>Counts every POST to Google's token endpoint and answers each with <see cref="FailWith"/>.</summary>
    private sealed class CountingGoogleHandler : HttpMessageHandler
    {
        private int _callCount;

        public HttpStatusCode FailWith { get; init; } = HttpStatusCode.BadRequest;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(
                new HttpResponseMessage(FailWith)
                {
                    Content = new StringContent(
                        """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
