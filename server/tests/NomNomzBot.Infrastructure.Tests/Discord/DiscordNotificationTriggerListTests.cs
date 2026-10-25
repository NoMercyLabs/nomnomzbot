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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Discord;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Proves the trigger list the Discord page reads: it is exactly the catalog the server fires from, each
/// entry carries a label key and the placeholders its message may use, and the three triggers no event
/// handler raises are never offered.
/// </summary>
public sealed class DiscordNotificationTriggerListTests
{
    private static DiscordNotificationConfigService Build() =>
        new(
            Substitute.For<IApplicationDbContext>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<ITemplateHelperValidator>(),
            Substitute.For<IEventBus>()
        );

    [Fact]
    public void GetTriggers_ListsTheCatalog_WithLabelKeyAndPlaceholders()
    {
        Result<IReadOnlyList<DiscordNotificationTriggerDto>> result = Build().GetTriggers();

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Select(trigger => trigger.Name)
            .Should()
            .Equal(DiscordConfigDefaults.NotificationTriggers);
        DiscordNotificationTriggerDto goLive = result.Value.Single(trigger =>
            trigger.Name == "go_live"
        );
        goLive.LabelKey.Should().Be("dashboard.discord.triggers.go_live");
        goLive.Placeholders.Should().Equal("broadcaster", "channel.name", "title", "game");
    }

    [Fact]
    public void EveryPlaceholderTheServerFills_CanBeSavedInAMessage()
    {
        ITemplateHelperValidator validator = DiscordTemplateTestSupport.CreateValidator();

        List<string> rejected =
        [
            .. DiscordConfigDefaults
                .NotificationTriggerCatalog.SelectMany(trigger =>
                    trigger.Placeholders.Select(placeholder => (trigger.Name, placeholder))
                )
                .Where(pair =>
                    validator
                        .Validate("{" + pair.placeholder + "}", TemplateHelperContext.Discord)
                        .IsFailure
                )
                .Select(pair => $"{pair.Name}:{pair.placeholder}"),
        ];

        rejected.Should().BeEmpty();
    }

    [Fact]
    public void GetTriggers_NeverOffersATriggerNoHandlerFires()
    {
        Result<IReadOnlyList<DiscordNotificationTriggerDto>> result = Build().GetTriggers();

        result
            .Value.Select(trigger => trigger.Name)
            .Should()
            .NotContain(DiscordConfigDefaults.UnfiredTriggers);
    }
}
