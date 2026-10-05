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
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves <c>{target.displayname}</c> is the stored display name of the @mention target (the old bot
/// read the DB DisplayName), falls back to the typed text for an unknown user, and leaves
/// <c>{target}</c> and <c>{target.name}</c> as they were.
/// </summary>
public sealed class TargetDisplayNameTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f301");

    private readonly TemplateResolver _resolver;

    public TargetDisplayNameTemplateResolverTests()
    {
        PronounGrammarTestDbContext db = PronounGrammarTestDbContext.New();
        db.Users.Add(
            new()
            {
                Id = Guid.Parse("0192b400-0000-7000-9000-00000000f302"),
                TwitchUserId = "222",
                Username = "sus_user",
                UsernameNormalized = "sus_user",
                DisplayName = "SuS_User",
            }
        );
        db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private Task<string> ResolveAsync(string template, string typedTarget) =>
        _resolver.ResolveAsync(
            template,
            new Dictionary<string, string> { ["target"] = typedTarget },
            Channel
        );

    [Fact]
    public async Task AKnownTarget_GivesTheStoredDisplayName_NotTheTypedText()
    {
        string resolved = await ResolveAsync(
            "{target.displayname}|{target}|{target.name}",
            "sus_user"
        );

        resolved.Should().Be("SuS_User|sus_user|sus_user");
    }

    [Fact]
    public async Task TheTypedCaseStillFindsTheStoredUser()
    {
        string resolved = await ResolveAsync("{target.displayname}", "SUS_USER");

        resolved.Should().Be("SuS_User");
    }

    [Fact]
    public async Task AnUnknownTarget_FallsBackToTheTypedText()
    {
        string resolved = await ResolveAsync("{target.displayname}|{target}", "NobodyHere");

        resolved.Should().Be("NobodyHere|NobodyHere");
    }

    [Fact]
    public async Task NoTargetAtAll_IsEmpty()
    {
        string resolved = await ResolveAsync("[{target.displayname}]", string.Empty);

        resolved.Should().Be("[]");
    }
}
