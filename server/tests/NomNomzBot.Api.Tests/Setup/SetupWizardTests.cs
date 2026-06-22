// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using NomNomzBot.Api.Models;

namespace NomNomzBot.Api.Tests.Setup;

/// <summary>
/// Proves the self-describing onboarding contract a dashboard renders from: the ordered required/optional steps,
/// each step's input fields + the exact redirect URI to register, the live completion mapping, and base-URL
/// normalization — so the onboarding UI needs no hardcoded knowledge of the flow.
/// </summary>
public sealed class SetupWizardTests
{
    [Fact]
    public void Build_returns_the_ordered_required_then_optional_steps()
    {
        SetupWizardDto wizard = SetupWizard.Build(
            false,
            false,
            false,
            false,
            "https://bot.example"
        );

        wizard.Complete.Should().BeFalse();
        wizard
            .Steps.Select(s => s.Key)
            .Should()
            .Equal("twitch_app", "platform_bot", "spotify", "discord");
        wizard
            .Steps.Where(s => s.Required)
            .Select(s => s.Key)
            .Should()
            .Equal("twitch_app", "platform_bot");
    }

    [Fact]
    public void Twitch_step_carries_the_exact_redirect_uri_and_credential_fields()
    {
        SetupStepDto twitch = SetupWizard
            .Build(false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");

        twitch
            .Instructions.Should()
            .Contain(i => i.Contains("https://bot.example/api/v1/auth/twitch/callback"));
        twitch.Fields.Select(f => f.Key).Should().Equal("clientId", "clientSecret");
        twitch.Fields.Single(f => f.Key == "clientSecret").Type.Should().Be("password");
        twitch
            .Action.Should()
            .Be(
                new SetupActionDto(
                    "save_credentials",
                    "PUT",
                    "/api/v1/system/setup/credentials/twitch",
                    null
                )
            );
    }

    [Fact]
    public void Build_reflects_live_completion_state()
    {
        SetupWizardDto wizard = SetupWizard.Build(true, true, false, false, "https://bot.example");

        wizard.Complete.Should().BeTrue(); // both required steps done
        wizard.Steps.Single(s => s.Key == "twitch_app").Complete.Should().BeTrue();
        wizard.Steps.Single(s => s.Key == "platform_bot").Status.Should().Be("connected");
        wizard.Steps.Single(s => s.Key == "spotify").Complete.Should().BeFalse();
    }

    [Fact]
    public void Build_normalizes_a_trailing_slash_in_the_base_url()
    {
        SetupStepDto twitch = SetupWizard
            .Build(false, false, false, false, "https://bot.example/")
            .Steps.First(s => s.Key == "twitch_app");

        twitch.Instructions.Should().Contain(i => i.Contains("https://bot.example/api/v1/auth"));
        twitch.Instructions.Should().NotContain(i => i.Contains("bot.example//api"));
    }
}
