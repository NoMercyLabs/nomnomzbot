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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// <c>{target.*}</c> only ever looked in our own <c>Users</c> table, so a real Twitch account that had
/// simply never spoken in this channel resolved to nothing at all — indistinguishable from a login someone
/// invented. Any command that treats those two cases differently (shout a first-time viewer vs. call out a
/// made-up name) could therefore never tell them apart, and every <c>{target.*}</c> placeholder rendered
/// empty for a viewer the channel had not seen yet.
/// <para>
/// The platform is asked only when the local row is missing, and the answer is not written to the database —
/// resolving a name for one chat line must not manufacture a viewer record.
/// </para>
/// </summary>
public sealed class UnseenTargetTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000d001");

    private readonly PronounGrammarTestDbContext _db = PronounGrammarTestDbContext.New();
    private readonly ITwitchUsersApi _twitch = Substitute.For<ITwitchUsersApi>();
    private readonly TemplateResolver _resolver;

    public UnseenTargetTemplateResolverTests()
    {
        _db.Users.Add(
            new User
            {
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        _db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton(_twitch);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static TwitchUser Profile(string login, string display, string id) =>
        new(
            Id: id,
            Login: login,
            DisplayName: display,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private Task<string> ResolveAsync(string template, string target) =>
        _resolver.ResolveAsync(
            template,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user.id"] = "111",
                ["target"] = target,
            },
            Channel
        );

    [Fact]
    public async Task A_real_viewer_this_channel_has_never_seen_still_resolves()
    {
        _twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([Profile("newbie", "Newbie", "999")])
            );

        string resolved = await ResolveAsync(
            "{target.id}|{target.name}|{target.displayname}|{target.known}",
            "newbie"
        );

        resolved
            .Should()
            .Be(
                "999|Newbie|Newbie|false",
                "a first-time viewer is a real person, and a command that names them must be able to"
            );
    }

    [Fact]
    public async Task A_login_nobody_owns_still_resolves_to_nothing()
    {
        _twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));

        string resolved = await ResolveAsync("{target.id}|{target.name}", "notarealaccount");

        // Unseen-but-real vs. invented: no id, the typed text as the name, never raw braces.
        resolved.Should().Be("|notarealaccount");
    }

    [Fact]
    public async Task A_viewer_we_already_know_never_costs_a_platform_call()
    {
        string resolved = await ResolveAsync("{target.id}", "alice");

        resolved.Should().Be("111");
        await _twitch.DidNotReceiveWithAnyArgs().GetUsersByLoginsAsync(default!);
    }

    [Fact]
    public async Task Resolving_a_name_never_manufactures_a_viewer_record()
    {
        _twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([Profile("newbie", "Newbie", "999")])
            );

        await ResolveAsync("{target.id}", "newbie");

        // Mentioning someone is not the same as them showing up; a template lookup must not create rows.
        _db.Users.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_platform_failure_leaves_the_placeholder_empty_rather_than_breaking_the_line()
    {
        _twitch
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("Twitch timed out.", "TIMEOUT"));

        string resolved = await ResolveAsync("hi {target.name}", "newbie");

        resolved
            .Should()
            .Be("hi newbie", "a failed lookup falls back to the typed name, never raw braces");
    }
}
