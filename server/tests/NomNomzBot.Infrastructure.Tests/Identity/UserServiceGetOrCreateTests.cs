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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the chat-ingest get-or-create seam (<see cref="IUserService.GetOrCreateAsync"/>) resolves through the
/// platform-agnostic <see cref="IUserIdentityService.ResolveUserAsync"/> (platform-identity §3.1) rather than a
/// Twitch-only <c>Users</c> lookup: an incoming chatter maps to a <c>User</c> AND always gets a
/// <c>UserIdentity</c> row, enriched with the real chat handle, idempotently, reusing any pre-identity user.
/// </summary>
public sealed class UserServiceGetOrCreateTests
{
    private static (IUserService Svc, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return (AuthTestBuilder.UserService(db, currentUser, scopeFactory), db);
    }

    [Fact]
    public async Task GetOrCreateAsync_creates_the_user_and_its_primary_twitch_identity()
    {
        (IUserService svc, AuthDbContext db) = Build();

        Result<UserDto> result = await svc.GetOrCreateAsync("500", "streamer", "Streamer");

        result.IsSuccess.Should().BeTrue();
        Guid userId = Guid.Parse(result.Value.Id);

        User user = await db.Users.SingleAsync(u => u.Id == userId);
        user.TwitchUserId.Should().Be("500");
        user.Username.Should().Be("streamer");

        // The identity row is what makes the chatter platform-agnostically resolvable — it must exist and carry
        // the real chat handle, not the resolver's numeric placeholder.
        UserIdentity identity = await db.UserIdentities.SingleAsync(i =>
            i.Provider == "twitch" && i.ProviderUserId == "500"
        );
        identity.UserId.Should().Be(userId);
        identity.IsPrimary.Should().BeTrue();
        identity.ProviderUsername.Should().Be("streamer");
    }

    [Fact]
    public async Task GetOrCreateAsync_is_idempotent_and_enriches_on_re_see()
    {
        (IUserService svc, AuthDbContext db) = Build();

        Result<UserDto> first = await svc.GetOrCreateAsync("500", "streamer", "Streamer");
        Result<UserDto> second = await svc.GetOrCreateAsync("500", "streamer_new", "Streamer New");

        second.Value.Id.Should().Be(first.Value.Id);
        (await db.Users.CountAsync(u => u.TwitchUserId == "500")).Should().Be(1);
        (await db.UserIdentities.CountAsync(i => i.ProviderUserId == "500")).Should().Be(1);
        // The re-see enriches both the user and the identity with the fresh handle.
        (await db.Users.SingleAsync(u => u.TwitchUserId == "500"))
            .Username.Should()
            .Be("streamer_new");
        (await db.UserIdentities.SingleAsync(i => i.ProviderUserId == "500"))
            .ProviderUsername.Should()
            .Be("streamer_new");
    }

    [Fact]
    public async Task GetOrCreateAsync_with_a_youtube_provider_mints_a_youtube_identity_not_a_twitch_one()
    {
        (IUserService svc, AuthDbContext db) = Build();

        Result<UserDto> result = await svc.GetOrCreateAsync(
            "UCabc123",
            "viewer",
            "Viewer",
            provider: "youtube"
        );

        result.IsSuccess.Should().BeTrue();
        Guid userId = Guid.Parse(result.Value.Id);

        // The identity lives in the youtube namespace — the id must never masquerade as a Twitch id.
        UserIdentity identity = await db.UserIdentities.SingleAsync(i =>
            i.ProviderUserId == "UCabc123"
        );
        identity.Provider.Should().Be("youtube");
        identity.UserId.Should().Be(userId);
        identity.IsPrimary.Should().BeTrue();
        (await db.Users.SingleAsync(u => u.Id == userId)).TwitchUserId.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreateAsync_reuses_a_pre_identity_user_and_backfills_its_identity()
    {
        (IUserService svc, AuthDbContext db) = Build();
        // A user created before the identity table (only TwitchUserId, no UserIdentity row).
        User legacy = new()
        {
            TwitchUserId = "777",
            Username = "legacy",
            UsernameNormalized = "legacy",
            DisplayName = "Legacy",
            Enabled = true,
        };
        db.Users.Add(legacy);
        await db.SaveChangesAsync();

        Result<UserDto> result = await svc.GetOrCreateAsync("777", "legacy", "Legacy");

        result.IsSuccess.Should().BeTrue();
        Guid.Parse(result.Value.Id).Should().Be(legacy.Id);
        (await db.Users.CountAsync(u => u.TwitchUserId == "777")).Should().Be(1);
        UserIdentity identity = await db.UserIdentities.SingleAsync(i => i.ProviderUserId == "777");
        identity.UserId.Should().Be(legacy.Id);
        identity.IsPrimary.Should().BeTrue();
    }
}
