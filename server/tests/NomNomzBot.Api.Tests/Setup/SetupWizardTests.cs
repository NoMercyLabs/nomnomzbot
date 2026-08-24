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
            false,
            false,
            "https://bot.example"
        );

        wizard.Complete.Should().BeFalse();
        wizard
            .Steps.Select(s => s.Key)
            .Should()
            .Equal("twitch_app", "platform_bot", "spotify", "discord", "youtube");
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
            .Build(false, false, false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");

        twitch
            .Instructions.Should()
            .Contain(i => i.Contains("https://bot.example/api/v1/auth/twitch/callback"));
        twitch.Fields.Select(f => f.Key).Should().Equal("clientId", "clientSecret");
        twitch.Fields.Single(f => f.Key == "clientSecret").Type.Should().Be("password");
        // The secret is OPTIONAL — only the client id is required to finish the step.
        twitch.Fields.Single(f => f.Key == "clientId").Required.Should().BeTrue();
        twitch.Fields.Single(f => f.Key == "clientSecret").Required.Should().BeFalse();
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
    public void Twitch_step_completes_on_a_client_id_alone_and_flags_the_redirect_enhancement()
    {
        // A client id with NO secret: the step is done (the bot runs via device-code) and the status names the
        // device-code readiness, leaving the redirect sign-in as an opt-in enhancement.
        SetupStepDto twitchDevice = SetupWizard
            .Build(true, false, false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");
        twitchDevice.Complete.Should().BeTrue();
        twitchDevice.Status.Should().Be("ready_device");

        // The same step with a secret present advertises the redirect login is available.
        SetupStepDto twitchRedirect = SetupWizard
            .Build(true, true, false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");
        twitchRedirect.Complete.Should().BeTrue();
        twitchRedirect.Status.Should().Be("ready_redirect");

        // No client id at all: still incomplete and missing.
        SetupStepDto twitchMissing = SetupWizard
            .Build(false, false, false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");
        twitchMissing.Complete.Should().BeFalse();
        twitchMissing.Status.Should().Be("missing");
    }

    [Fact]
    public void Build_is_complete_on_a_secret_free_twitch_client_id_alone_with_no_bot()
    {
        // Onboarding (Complete) is deployment-level configuration ONLY: a client id makes it done, full stop.
        // The bot is per-channel work that happens after login — it must never gate onboarding completion,
        // even though the wizard's own "platform_bot" step still separately tracks its own live state.
        SetupWizardDto wizard = SetupWizard.Build(
            hasTwitchClientId: true,
            hasTwitchSecret: false,
            hasPlatformBot: false,
            hasSpotify: false,
            hasDiscord: false,
            hasYouTube: false,
            "https://bot.example"
        );

        wizard.Complete.Should().BeTrue();
        wizard.Steps.Single(s => s.Key == "twitch_app").Complete.Should().BeTrue();
        wizard.Steps.Single(s => s.Key == "platform_bot").Complete.Should().BeFalse();
        wizard.Steps.Single(s => s.Key == "platform_bot").Status.Should().Be("disconnected");
        wizard.Steps.Single(s => s.Key == "spotify").Complete.Should().BeFalse();
    }

    [Fact]
    public void Build_is_not_complete_with_no_platform_credentials_at_all()
    {
        SetupWizardDto wizard = SetupWizard.Build(
            hasTwitchClientId: false,
            hasTwitchSecret: false,
            hasPlatformBot: false,
            hasSpotify: false,
            hasDiscord: false,
            hasYouTube: false,
            "https://bot.example"
        );

        wizard.Complete.Should().BeFalse();
    }

    [Fact]
    public void Build_normalizes_a_trailing_slash_in_the_base_url()
    {
        SetupStepDto twitch = SetupWizard
            .Build(false, false, false, false, false, false, "https://bot.example/")
            .Steps.First(s => s.Key == "twitch_app");

        twitch.Instructions.Should().Contain(i => i.Contains("https://bot.example/api/v1/auth"));
        twitch.Instructions.Should().NotContain(i => i.Contains("bot.example//api"));
    }
}
