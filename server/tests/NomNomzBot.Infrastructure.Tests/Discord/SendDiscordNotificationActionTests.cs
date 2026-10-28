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
