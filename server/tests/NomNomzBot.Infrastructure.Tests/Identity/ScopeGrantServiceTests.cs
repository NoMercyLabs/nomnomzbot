// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves progressive, grant-aware scopes (identity-auth §3.4a): enabling a feature whose scopes are
/// already granted needs NO OAuth (no URL); enabling one that needs more returns an incremental authorize
/// URL requesting the union; and reconciling to a narrower granted set drops the missing scope, emits
/// <see cref="ScopesDroppedEvent"/>, and reports exactly the features it disabled.
/// </summary>
public sealed class ScopeGrantServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000e5");

    private static (ScopeGrantService Service, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        RecordingEventBus bus = new();
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(out _);
        // No DB-vaulted credential row is seeded here, so the credentials provider falls back to these
        // config values — proving the incremental authorize URL carries the resolved app client id.
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["App:BaseUrl"] = "https://api.example.test",
                    ["Twitch:ClientId"] = "client-xyz",
                    ["Twitch:ClientSecret"] = "s",
                }
            )
            .Build();
        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            config
        );
        ScopeGrantService service = new(db, bus, credentials, new NoopScopeNotifications(), config);
        return (service, db, bus);
    }

    private static async Task<Guid> SeedTwitchConnectionAsync(
        AuthDbContext db,
        params string[] scopes
    )
    {
        IntegrationConnection connection = new()
        {
            BroadcasterId = Tenant,
            Provider = AuthEnums.IntegrationProvider.Twitch,
            Status = AuthEnums.IntegrationStatus.Connected,
            Scopes = [.. scopes],
        };
        db.IntegrationConnections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    // ─── grant-aware enable ────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureFeatureScopes_WhenAlreadyGranted_NeedsNoOAuth()
    {
        (ScopeGrantService service, AuthDbContext db, _) = Build();
        await SeedTwitchConnectionAsync(db, "channel:read:subscriptions");

        Result<ScopeGrantState> result = await service.EnsureFeatureScopesAsync(
            Tenant,
            "subscriptions"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyGranted.Should().BeTrue();
        result.Value.IncrementalAuthorizeUrl.Should().BeNull();
        result.Value.MissingScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureFeatureScopes_WhenMissing_ReturnsIncrementalUrlWithTheUnion()
    {
        (ScopeGrantService service, AuthDbContext db, _) = Build();
        await SeedTwitchConnectionAsync(db, "channel:read:subscriptions");

        // "polls" needs channel:read:polls + channel:manage:polls — neither granted.
        Result<ScopeGrantState> result = await service.EnsureFeatureScopesAsync(Tenant, "polls");

        result.Value.AlreadyGranted.Should().BeFalse();
        result.Value.MissingScopes.Should().Contain(["channel:read:polls", "channel:manage:polls"]);

        string url = result.Value.IncrementalAuthorizeUrl!;
        url.Should().StartWith("https://id.twitch.tv/oauth2/authorize");
        // The request carries the UNION: the already-granted scope plus the newly needed ones.
        Uri.UnescapeDataString(url).Should().Contain("channel:read:subscriptions");
        Uri.UnescapeDataString(url).Should().Contain("channel:read:polls");
        Uri.UnescapeDataString(url).Should().Contain("channel:manage:polls");
        url.Should().Contain("client_id=client-xyz");
    }

    // ─── drop detection ────────────────────────────────────────────────────────

    [Fact]
    public async Task Reconcile_WhenScopeDropped_EmitsScopesDropped_AndUpdatesStoredScopes()
    {
        (ScopeGrantService service, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid connectionId = await SeedTwitchConnectionAsync(
            db,
            "channel:read:subscriptions",
            "channel:read:polls",
            "channel:manage:polls"
        );

        // Provider now reports a narrower grant — the polls scopes are gone.
        Result<IReadOnlyList<string>> dropped = await service.ReconcileGrantedScopesAsync(
            connectionId,
            ["channel:read:subscriptions"]
        );

        dropped.IsSuccess.Should().BeTrue();
        dropped.Value.Should().Contain(["channel:read:polls", "channel:manage:polls"]);

        // The stored grant set is now the authoritative actual set.
        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Id == connectionId);
        connection.Scopes.Should().ContainSingle().Which.Should().Be("channel:read:subscriptions");

        ScopesDroppedEvent evt = bus.Published.OfType<ScopesDroppedEvent>().Single();
        evt.DroppedScopes.Should().Contain(["channel:read:polls", "channel:manage:polls"]);
        // The "polls" feature, satisfied before, is no longer satisfied → disabled.
        evt.DisabledFeatures.Should().Contain("polls");
        evt.BroadcasterId.Should().Be(Tenant);
    }

    [Fact]
    public async Task Reconcile_WhenNothingDropped_EmitsNoEvent()
    {
        (ScopeGrantService service, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid connectionId = await SeedTwitchConnectionAsync(db, "channel:read:subscriptions");

        Result<IReadOnlyList<string>> dropped = await service.ReconcileGrantedScopesAsync(
            connectionId,
            ["channel:read:subscriptions", "bits:read"]
        );

        dropped.Value.Should().BeEmpty();
        bus.Published.OfType<ScopesDroppedEvent>().Should().BeEmpty();

        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync(c => c.Id == connectionId);
        connection.Scopes.Should().Contain(["channel:read:subscriptions", "bits:read"]);
    }
}

/// <summary>A no-op scope-notification double — the grant tests exercise drop detection, not the gap surface.</summary>
internal sealed class NoopScopeNotifications : IScopeNotificationService
{
    public Task<Result<bool>> RecordMissingScopeAsync(
        Guid broadcasterId,
        string scope,
        string? feature,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success(false));

    public Task<Result<MissingScopesDto>> GetMissingScopesAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success(new MissingScopesDto("connected", [])));

    public Task<Result<int>> NotifyPendingAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success(0));

    public Task<Result<IReadOnlyList<string>>> ClearResolvedAsync(
        Guid broadcasterId,
        IReadOnlyCollection<string> grantedScopes,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));

    public Task<Result<IReadOnlyList<string>>> BuildRegrantScopeSetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));
}
