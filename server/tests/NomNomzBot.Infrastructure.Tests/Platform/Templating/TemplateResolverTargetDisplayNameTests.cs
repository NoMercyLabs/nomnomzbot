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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.ViewerData;
using NSubstitute;
using InfraViewerDataService = NomNomzBot.Infrastructure.ViewerData.ViewerDataService;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// A viewer types the display name they see in chat (for example a Korean one), not the login. The
/// <c>{target.*}</c> placeholders must resolve that name to the stored viewer, as the command sites do.
/// </summary>
public sealed class TemplateResolverTargetDisplayNameTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-8000-00000000c101");
    private static readonly Guid Keira = Guid.Parse("0192b400-0000-7000-8000-00000000a101");
    private const string KeiraDisplayName = "케이라라";

    private static User KeiraUser() =>
        new()
        {
            Id = Keira,
            TwitchUserId = "777",
            Username = "keiraravt",
            UsernameNormalized = "keiraravt",
            DisplayName = KeiraDisplayName,
        };

    private static TemplateResolver Build(IApplicationDbContext db, ServiceCollection services)
    {
        services.AddSingleton(db);
        ServiceProvider provider = services.BuildServiceProvider();
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static TemplateResolver ResolverWithUser()
    {
        PronounGrammarTestDbContext db = PronounGrammarTestDbContext.New();
        db.Users.Add(KeiraUser());
        db.SaveChanges();
        return Build(db, new ServiceCollection());
    }

    private static TemplateResolver ResolverWithViewerData()
    {
        ViewerDataTestDbContext db = ViewerDataTestDbContext.New();
        db.Users.Add(KeiraUser());
        db.ViewerData.Add(
            new ViewerDatum
            {
                BroadcasterId = Channel,
                ViewerUserId = Keira,
                Key = "deaths",
                Value = "5",
            }
        );
        db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IViewerDataService>(
            new InfraViewerDataService(db, TimeProvider.System)
        );
        services.AddSingleton<INamedCounterService>(new NamedCounterService(db));
        return Build(db, services);
    }

    private static Dictionary<string, string> Seeds(string target) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = "111",
            ["user.provider"] = "twitch",
            ["target"] = target,
        };

    [Fact]
    public async Task TargetName_ResolvesADisplayNameToTheViewersLogin_AndMarksItKnown()
    {
        string resolved = await ResolverWithUser()
            .ResolveAsync(
                "{target.name}|{target.known}|{target.id}|{target.displayname}",
                Seeds(KeiraDisplayName),
                Channel
            );

        resolved.Should().Be("keiraravt|true|777|" + KeiraDisplayName);
    }

    [Fact]
    public async Task TargetData_ResolvesTheStoredValueOfTheViewerTheDisplayNameBelongsTo()
    {
        string resolved = await ResolverWithViewerData()
            .ResolveAsync("{target.data.deaths}", Seeds("@" + KeiraDisplayName), Channel);

        resolved.Should().Be("5");
    }
}
