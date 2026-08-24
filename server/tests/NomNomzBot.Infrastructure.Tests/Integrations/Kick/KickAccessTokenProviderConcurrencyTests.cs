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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.Kick;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Integrations.Kick;

/// <summary>
/// S036 — Kick is OAuth 2.1 and rotates the refresh token on EVERY grant, so two callers racing a refresh
/// for the same connection is the sharpest version of the stampede bug: the loser posting the token the
/// winner already spent would fail outright, or a write race could vault the loser's stale pair over the
/// winner's fresh one. Proven over the REAL <see cref="NomNomzBot.Infrastructure.Identity.IntegrationTokenVault"/>
/// with two INDEPENDENT DbContext instances sharing one SQLite store (the same shape as two concurrent
/// requests each getting their own scoped DbContext) and one shared <see cref="ConnectionRefreshGate"/>.
/// </summary>
public sealed class KickAccessTokenProviderConcurrencyTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000c1");
    private const string ExternalId = "998877";

    [Fact]
    public async Task TwoConcurrentGetAsyncCalls_ForTheSameConnection_HitKickExactlyOnce_AndBothGetTheSameToken()
    {
        string dbName = Guid.NewGuid().ToString();
        ConnectionRefreshGate gate = new();
        CountingKickHandler wire = new() { HoldFirstCall = true };

        KickAccessTokenProvider providerA = await BuildAsync(dbName, gate, wire);
        KickAccessTokenProvider providerB = await BuildAsync(dbName, gate, wire);

        // Force a REAL race: t1 is made to block INSIDE the HTTP call (holding the gate), so t2 is
        // guaranteed to arrive while t1's refresh is still in flight and block on the SAME gate key.
        Task<KickAccess?> t1 = providerA.GetAsync(Broadcaster);
        await WaitUntilAsync(() => wire.CallCount >= 1); // t1 is now blocked inside the HTTP call
        Task<KickAccess?> t2 = providerB.GetAsync(Broadcaster);
        await Task.Delay(50); // give t2 time to reach the vault check + block on the gate
        wire.Release();

        KickAccess?[] results = await Task.WhenAll(t1, t2);

        wire.CallCount.Should().Be(1, "the second caller must reuse the winner's re-vaulted token");
        results[0].Should().NotBeNull();
        results[1].Should().NotBeNull();
        results[0]!.AccessToken.Should().Be("kick-access-1");
        results[1]!.AccessToken.Should().Be("kick-access-1");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    private static async Task<KickAccessTokenProvider> BuildAsync(
        string dbName,
        ConnectionRefreshGate gate,
        CountingKickHandler wire
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
                    Provider = AuthEnums.Platform.Kick,
                    ExternalChannelId = ExternalId,
                    Name = "kick-streamer",
                    NameNormalized = "kick-streamer",
                    OwnerUserId = Guid.NewGuid(),
                }
            );
        }

        if (!db.Configurations.Any(c => c.Key == "kick.client_id"))
        {
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "kick.client_id",
                    Value = "kick-app-id",
                }
            );
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "kick.client_secret",
                    SecureValue = await protector.ProtectAsync(
                        "kick-app-secret",
                        SystemCredentialsProvider.ContextFor("kick.client_secret")
                    ),
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
                c.Provider == AuthEnums.IntegrationProvider.Kick
                && c.ProviderAccountId == ExternalId
            )
        )
        {
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.Kick,
                    ProviderAccountId: ExternalId,
                    ProviderAccountName: "kick-streamer",
                    Scopes: ["chat:write"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            // Already expired — GetAsync must refresh on the first call.
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new("old-access", "old-refresh", null, DateTime.UtcNow.AddMinutes(-10))
            );
        }

        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            new ConfigurationBuilder().Build()
        );

        return new(
            db,
            vault,
            credentials,
            TimeProvider.System,
            new SingleClientFactory(wire),
            NullLogger<KickAccessTokenProvider>.Instance,
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

    /// <summary>
    /// Counts every POST. When <see cref="HoldFirstCall"/> is set, the FIRST call blocks until
    /// <see cref="Release"/> is called — forces a REAL race instead of two calls that happen to run one
    /// after the other (which an all-synchronous in-memory test can otherwise produce, defeating the point
    /// of a concurrency test).
    /// </summary>
    private sealed class CountingKickHandler : HttpMessageHandler
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
                    $$"""{"access_token":"kick-access-{{callNumber}}","refresh_token":"kick-refresh-{{callNumber}}","expires_in":3600}""",
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
