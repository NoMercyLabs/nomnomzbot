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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord;
using NomNomzBot.Infrastructure.Discord.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Discord;

public sealed class SendDiscordNotificationActionTests
{
    [Fact]
    public void TriggerType_is_picked_from_the_rule_triggers_never_typed()
    {
        SendDiscordNotificationAction action = new(
            Substitute.For<IDiscordNotificationDispatcher>()
        );

        PipelineActionFieldDescriptor field = action.Fields.Single(f => f.Name == "trigger_type");

        field.Kind.Should().Be(PipelineActionFieldKind.Enum);
        field.Required.Should().BeTrue();
        field.Options.Should().Equal(DiscordConfigDefaults.NotificationTriggers);
    }
}
