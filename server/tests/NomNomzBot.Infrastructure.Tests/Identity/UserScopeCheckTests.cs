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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// A moderator or viewer holds one personal Twitch connection bound to their own account and to no channel.
/// The user-scope check reads THAT connection's granted scopes. The tenant check (<c>HasScopeAsync</c>) matches
/// a connection by broadcaster id, so passing it a user id finds nothing and reports every scope missing.
/// </summary>
public sealed class UserScopeCheckTests
{
    private const string Read = "user:read:chat";
    private const string Write = "user:write:chat";

    [Fact]
    public async Task The_check_reads_the_users_own_connection_scopes()
    {
        (TwitchTokenResolver resolver, User user) = await BuildAsync([Write]);

        (await resolver.HasUserScopeAsync(user.Id, Write)).Should().BeTrue();
        (await resolver.HasUserScopeAsync(user.Id, Read)).Should().BeFalse();
    }

    [Fact]
    public async Task The_tenant_check_cannot_see_a_users_personal_connection()
    {
        (TwitchTokenResolver resolver, User user) = await BuildAsync([Write]);

        (await resolver.HasScopeAsync(user.Id, Write))
            .Should()
            .BeFalse("a personal connection is filed under no broadcaster");
    }

    [Fact]
    public async Task A_user_with_no_connection_has_no_scope()
    {
        (TwitchTokenResolver resolver, _) = await BuildAsync([Write]);

        (await resolver.HasUserScopeAsync(Guid.NewGuid(), Write)).Should().BeFalse();
    }

    private static async Task<(TwitchTokenResolver Resolver, User User)> BuildAsync(string[] scopes)
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
        User user = new()
        {
            TwitchUserId = "tw-mod",
            Platform = AuthEnums.Platform.Twitch,
            Username = "mod",
            UsernameNormalized = "mod",
            DisplayName = "mod",
            Enabled = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Guid connectionId = (
            await vault.UpsertConnectionAsync(
                new(
                    null,
                    AuthEnums.IntegrationProvider.Twitch,
                    user.TwitchUserId,
                    user.Username,
                    scopes,
                    "cid",
                    IsByok: false,
                    user.Id,
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

        TwitchTokenResolver resolver = new(
            db,
            vault,
            Substitute.For<ITwitchAuthService>(),
            Substitute.For<ITwitchAppTokenProvider>(),
            new RecordingEventBus(),
            Substitute.For<IDeploymentProfileService>()
        );
        return (resolver, user);
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
