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
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves a typed chat target resolves to a login: a display name (here Korean) maps to its owner's
/// login, a login-shaped name needs no user row, and an unknown or shared display name is left as typed.
/// </summary>
public sealed class MentionTargetResolverTests
{
    private static async Task<AuthDbContext> SeedAsync(
        params (string Login, string Display)[] users
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        foreach ((string login, string display) in users)
            db.Users.Add(
                new()
                {
                    Id = Guid.NewGuid(),
                    Username = login,
                    UsernameNormalized = login.ToUpperInvariant(),
                    DisplayName = display,
                    TwitchUserId = Guid.NewGuid().ToString("N"),
                }
            );
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_display_name_with_one_matching_user_resolves_to_that_users_login()
    {
        AuthDbContext db = await SeedAsync(("keiraravt", "케이라라"), ("other", "Other"));

        string login = await new MentionTargetResolver(db).ResolveLoginAsync("@케이라라");

        login.Should().Be("keiraravt");
    }

    [Fact]
    public async Task A_display_name_matches_regardless_of_letter_case()
    {
        AuthDbContext db = await SeedAsync(("zoe", "Zoë Müller"));

        string login = await new MentionTargetResolver(db).ResolveLoginAsync("@ZOË MÜLLER");

        login.Should().Be("zoe");
    }

    [Fact]
    public async Task A_login_shaped_name_comes_back_lowercased_without_needing_a_user_row()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        string login = await new MentionTargetResolver(db).ResolveLoginAsync("@SomeBody_1");

        login.Should().Be("somebody_1");
    }

    [Fact]
    public async Task Two_users_sharing_a_display_name_leave_the_input_unchanged()
    {
        AuthDbContext db = await SeedAsync(("first", "케이라라"), ("second", "케이라라"));

        string login = await new MentionTargetResolver(db).ResolveLoginAsync("@케이라라");

        login.Should().Be("케이라라");
    }

    [Fact]
    public async Task An_unknown_display_name_leaves_the_input_unchanged()
    {
        AuthDbContext db = await SeedAsync(("keiraravt", "케이라라"));

        string login = await new MentionTargetResolver(db).ResolveLoginAsync("@아무도");

        login.Should().Be("아무도");
    }

    [Fact]
    public async Task No_target_resolves_to_an_empty_string()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();

        (await new MentionTargetResolver(db).ResolveLoginAsync(null)).Should().BeEmpty();
        (await new MentionTargetResolver(db).ResolveLoginAsync("  @ ")).Should().BeEmpty();
    }
}
