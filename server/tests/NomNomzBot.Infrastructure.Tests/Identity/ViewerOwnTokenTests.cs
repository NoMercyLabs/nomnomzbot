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

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A viewer or moderator who signs in keeps their own Twitch token, bound to their own account and to no
/// channel. Two such users each keep a separate row, and the composer's send-as-user lookup hands each user
/// their own token, never another user's.
/// </summary>
public sealed class ViewerOwnTokenTests
{
    [Fact]
    public async Task Two_viewers_keep_separate_tokens_and_each_resolves_to_their_own()
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

        User alice = await AddUserAsync(db, "tw-alice");
        User bob = await AddUserAsync(db, "tw-bob");
        await ConnectViewerAsync(vault, alice, "alice-token");
        await ConnectViewerAsync(vault, bob, "bob-token");

        (
            await db
                .IntegrationConnections.IgnoreQueryFilters()
                .CountAsync(c => c.BroadcasterId == null)
        )
            .Should()
            .Be(2);

        TwitchTokenResolver resolver = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchAppTokenProvider>(),
            new RecordingEventBus(),
            Substitute.For<IDeploymentProfileService>()
        );

        Result<TwitchAccessContext> aliceToken = await resolver.GetUserTokenAsync(alice.Id);
        Result<TwitchAccessContext> bobToken = await resolver.GetUserTokenAsync(bob.Id);

        aliceToken.Value.AccessToken.Should().Be("alice-token");
        bobToken.Value.AccessToken.Should().Be("bob-token");
        aliceToken.Value.TokenBucketKey.Should().NotBe(bobToken.Value.TokenBucketKey);
    }

    private static async Task<User> AddUserAsync(AuthDbContext db, string twitchUserId)
    {
        User user = new()
        {
            TwitchUserId = twitchUserId,
            Platform = AuthEnums.Platform.Twitch,
            Username = twitchUserId,
            UsernameNormalized = twitchUserId,
            DisplayName = twitchUserId,
            Enabled = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task ConnectViewerAsync(
        IIntegrationTokenVault vault,
        User user,
        string accessToken
    )
    {
        Result<IntegrationConnectionDto> connection = await vault.UpsertConnectionAsync(
            new(
                null,
                AuthEnums.IntegrationProvider.Twitch,
                user.TwitchUserId,
                user.Username,
                ["user:write:chat"],
                "cid",
                IsByok: false,
                user.Id,
                SettingsJson: null
            )
        );
        connection.IsSuccess.Should().BeTrue();
        Result stored = await vault.StoreTokensAsync(
            connection.Value.Id,
            new(accessToken, "refresh", AppToken: null, DateTime.UtcNow.AddHours(4)),
            ["user:write:chat"]
        );
        stored.IsSuccess.Should().BeTrue();
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
