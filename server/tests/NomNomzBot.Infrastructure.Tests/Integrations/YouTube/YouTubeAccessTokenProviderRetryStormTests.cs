// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.YouTube;
using NomNomzBot.Infrastructure.Notifications.Sources;
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
    public async Task Invalid_grant_marks_needs_reauth_at_once_and_stops_the_calls()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new() { FailWith = HttpStatusCode.BadRequest };
        Harness harness = await Harness.BuildAsync(wire, clock);

        for (int i = 0; i < 5; i++)
        {
            await harness.Provider.GetAccessTokenAsync(Broadcaster);
            clock.Advance(TimeSpan.FromMinutes(11));
        }

        wire.CallCount.Should().Be(1);
        IntegrationConnection connection = await harness.ReadConnectionAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
    }

    [Fact]
    public async Task A_401_invalid_client_refresh_marks_needs_reauth_and_puts_one_reconnect_item_in_the_inbox()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new()
        {
            FailWith = HttpStatusCode.Unauthorized,
            Body =
                """{"error":"invalid_client","error_description":"The OAuth client was not found."}""",
        };
        Harness harness = await Harness.BuildAsync(wire, clock);

        for (int i = 0; i < 4; i++)
        {
            (await harness.Provider.GetAccessTokenAsync(Broadcaster)).Should().BeNull();
            clock.Advance(TimeSpan.FromMinutes(15));
        }

        IntegrationConnection connection = await harness.ReadConnectionAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
        wire.CallCount.Should().Be(1, "a refused grant stops the retry");
        harness.Bus.Published.OfType<IntegrationNeedsReauthEvent>().Should().ContainSingle();
        List<ActionRequiredItemDto> items = await harness.InboxItemsAsync();
        items.Should().ContainSingle();
        items[0].Kind.Should().Be("integration_token_dead");
        items[0].DeepLinkRoute.Should().Be("integrations");
    }

    [Fact]
    public async Task A_503_refresh_is_transient_and_asks_the_owner_for_nothing()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new()
        {
            FailWith = HttpStatusCode.ServiceUnavailable,
            Body = "<html>unavailable</html>",
        };
        Harness harness = await Harness.BuildAsync(wire, clock);

        for (int i = 0; i < 5; i++)
        {
            await harness.Provider.GetAccessTokenAsync(Broadcaster);
            clock.Advance(TimeSpan.FromMinutes(15));
        }

        wire.CallCount.Should().Be(5, "a transient failure keeps retrying");
        IntegrationConnection connection = await harness.ReadConnectionAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        connection.ConsecutiveFailureCount.Should().Be(0);
        connection.LastErrorAt.Should().NotBeNull();
        (await harness.InboxItemsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Repeated_401s_without_a_reason_count_up_to_needs_reauth_with_one_inbox_item()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        CountingGoogleHandler wire = new() { FailWith = HttpStatusCode.Unauthorized, Body = "" };
        Harness harness = await Harness.BuildAsync(wire, clock);

        for (int i = 0; i < 6; i++)
        {
            await harness.Provider.GetAccessTokenAsync(Broadcaster);
            clock.Advance(TimeSpan.FromMinutes(15));
        }

        IntegrationConnection connection = await harness.ReadConnectionAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
        connection.ConsecutiveFailureCount.Should().Be(3);
        wire.CallCount.Should().Be(3);
        harness.Bus.Published.OfType<IntegrationNeedsReauthEvent>().Should().ContainSingle();
        (await harness.InboxItemsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task A_second_refusal_does_not_mint_a_second_inbox_item()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        Harness harness = await Harness.BuildAsync(new CountingGoogleHandler(), clock);

        await harness.Vault.MarkRefreshRefusedAsync(harness.ConnectionId, "invalid_client");
        string firstKey = (await harness.InboxItemsAsync()).Single().Id;
        clock.Advance(TimeSpan.FromMinutes(15));
        await harness.Vault.MarkRefreshRefusedAsync(harness.ConnectionId, "invalid_client");

        List<ActionRequiredItemDto> items = await harness.InboxItemsAsync();
        items.Should().ContainSingle();
        items[0].Id.Should().Be(firstKey, "a dismissed prompt must not come back under a new key");
        harness.Bus.Published.OfType<IntegrationNeedsReauthEvent>().Should().ContainSingle();
    }

    private sealed class Harness
    {
        public required YouTubeAccessTokenProvider Provider { get; init; }
        public required IntegrationTokenVault Vault { get; init; }
        public required AuthDbContext Db { get; init; }
        public required RecordingEventBus Bus { get; init; }
        public required Guid ConnectionId { get; init; }

        public Task<IntegrationConnection> ReadConnectionAsync() =>
            Db.IntegrationConnections.AsNoTracking().SingleAsync(c => c.Id == ConnectionId);

        public async Task<List<ActionRequiredItemDto>> InboxItemsAsync()
        {
            Result<List<ActionRequiredItemDto>> result = await new DeadIntegrationTokenSource(
                Db
            ).GetItemsAsync(Broadcaster, new HashSet<string>());
            return result.Value;
        }

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
            RecordingEventBus bus = new();
            IntegrationTokenVault vault = new(
                db,
                protector,
                keys,
                new NoopScopeGrantService(),
                bus,
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
                Bus = bus,
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
        public string Body { get; init; } =
            """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""";
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
                    Content = new StringContent(Body, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
