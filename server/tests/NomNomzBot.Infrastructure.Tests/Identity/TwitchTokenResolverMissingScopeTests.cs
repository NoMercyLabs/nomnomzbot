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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves a missing scope that is already known for a broadcaster's current grant is reported once, not on every
/// pre-check: the snapshot builders hit <c>HasScopeAsync</c> every few minutes and each failed check used to
/// publish a fresh re-auth event (journal row, DB write, inbox recompute). A changed grant reports again.
/// </summary>
public sealed class TwitchTokenResolverMissingScopeTests
{
    private const string Subs = "channel:read:subscriptions";
    private const string Vips = "channel:read:vips";
    private const string Mods = "moderation:read";

    private static (
        TwitchTokenResolver Resolver,
        IntegrationTokenVault Vault,
        RecordingEventBus Bus
    ) Build()
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
            new PassthroughScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        RecordingEventBus bus = new();
        TwitchTokenResolver resolver = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchAppTokenProvider>(),
            bus
        );
        return (resolver, vault, bus);
    }

    private static async Task GrantAsync(
        IntegrationTokenVault vault,
        Guid broadcasterId,
        params string[] scopes
    )
    {
        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new(
                    broadcasterId,
                    AuthEnums.IntegrationProvider.Twitch,
                    "twitch-user-1",
                    "login",
                    scopes,
                    ClientId: "client",
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            )
        )
            .Value
            .Id;
        await vault.StoreTokensAsync(
            connectionId,
            new("access", "refresh", AppToken: null, DateTime.UtcNow.AddHours(1)),
            scopes
        );
    }

    private static List<TwitchHelixReauthRequiredEvent> Reauths(RecordingEventBus bus) =>
        [.. bus.Published.OfType<TwitchHelixReauthRequiredEvent>()];

    [Fact]
    public async Task The_same_missing_scope_checked_twice_publishes_one_event()
    {
        Guid broadcaster = Guid.NewGuid();
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, RecordingEventBus bus) =
            Build();
        await GrantAsync(vault, broadcaster, Subs);

        (await resolver.HasScopeAsync(broadcaster, Mods)).Should().BeFalse();
        (await resolver.HasScopeAsync(broadcaster, Mods)).Should().BeFalse();

        List<TwitchHelixReauthRequiredEvent> events = Reauths(bus);
        events.Should().ContainSingle();
        events[0].BroadcasterId.Should().Be(broadcaster);
        events[0].Reason.Should().Be(TwitchErrorCodes.MissingScope);
        events[0].MissingScope.Should().Be(Mods);
    }

    [Fact]
    public async Task A_different_missing_scope_publishes_its_own_event()
    {
        Guid broadcaster = Guid.NewGuid();
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, RecordingEventBus bus) =
            Build();
        await GrantAsync(vault, broadcaster, Subs);

        await resolver.HasScopeAsync(broadcaster, Mods);
        await resolver.HasScopeAsync(broadcaster, Vips);
        await resolver.HasScopeAsync(broadcaster, Mods);

        Reauths(bus).Select(e => e.MissingScope).Should().Equal(Mods, Vips);
    }

    [Fact]
    public async Task A_changed_grant_publishes_the_still_missing_scope_again()
    {
        Guid broadcaster = Guid.NewGuid();
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, RecordingEventBus bus) =
            Build();
        await GrantAsync(vault, broadcaster, Subs);
        await resolver.HasScopeAsync(broadcaster, Mods);

        await GrantAsync(vault, broadcaster, Subs, Vips);
        await resolver.HasScopeAsync(broadcaster, Mods);
        await resolver.HasScopeAsync(broadcaster, Mods);

        Reauths(bus).Select(e => e.MissingScope).Should().Equal(Mods, Mods);
    }

    [Fact]
    public async Task A_scope_that_is_granted_publishes_nothing()
    {
        Guid broadcaster = Guid.NewGuid();
        (TwitchTokenResolver resolver, IntegrationTokenVault vault, RecordingEventBus bus) =
            Build();
        await GrantAsync(vault, broadcaster, Subs);

        (await resolver.HasScopeAsync(broadcaster, Subs)).Should().BeTrue();

        Reauths(bus).Should().BeEmpty();
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
        ) => Task.FromResult(Result.Success(actualScopes));
    }
}
