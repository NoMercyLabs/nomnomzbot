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
/// normalization — so the onboarding UI needs no hardcoded knowledge of the flow. Also proves the fix for the
/// virgin-machine login-lockout bug: a client id being usable (READY, e.g. the shipped public default) is never
/// enough to complete the step or onboarding — only a RECORDED DECISION (BYOC saved, or the shared app
/// explicitly chosen) does.
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
            .Build(false, false, false, false, false, false, false, "https://bot.example")
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
        // The one-click shared-app alternative is always offered alongside BYOC.
        twitch
            .UseSharedAction.Should()
            .Be(
                new SetupActionDto(
                    "use_shared",
                    "POST",
                    "/api/v1/system/setup/credentials/twitch/use-shared",
                    null
                )
            );
    }

    [Fact]
    public void Twitch_step_status_reflects_usability_even_without_a_recorded_decision()
    {
        // A client id resolves (any source — including the shipped public default) but NO decision has been
        // recorded yet: Status still reports the device-code flow is usable (what the UI shows), while Complete
        // stays false (the reproduced bug's exact shape — READY must never imply DONE).
        SetupStepDto twitchDevice = SetupWizard
            .Build(
                hasTwitchClientId: true,
                hasTwitchSecret: false,
                hasTwitchDecision: false,
                hasPlatformBot: false,
                hasSpotify: false,
                hasDiscord: false,
                hasYouTube: false,
                "https://bot.example"
            )
            .Steps.First(s => s.Key == "twitch_app");
        twitchDevice.Status.Should().Be("ready_device");
        twitchDevice.Complete.Should().BeFalse();

        // A secret is also present (redirect enhancement usable) — still no decision, still not complete.
        SetupStepDto twitchRedirect = SetupWizard
            .Build(
                hasTwitchClientId: true,
                hasTwitchSecret: true,
                hasTwitchDecision: false,
                hasPlatformBot: false,
                hasSpotify: false,
                hasDiscord: false,
                hasYouTube: false,
                "https://bot.example"
            )
            .Steps.First(s => s.Key == "twitch_app");
        twitchRedirect.Status.Should().Be("ready_redirect");
        twitchRedirect.Complete.Should().BeFalse();

        // No client id at all: still incomplete and missing.
        SetupStepDto twitchMissing = SetupWizard
            .Build(false, false, false, false, false, false, false, "https://bot.example")
            .Steps.First(s => s.Key == "twitch_app");
        twitchMissing.Complete.Should().BeFalse();
        twitchMissing.Status.Should().Be("missing");
    }

    [Fact]
    public void Twitch_step_completes_only_once_a_decision_is_recorded()
    {
        // A recorded decision (BYOC saved, or the shared app chosen) completes the step regardless of Status.
        SetupStepDto twitchDecided = SetupWizard
            .Build(
                hasTwitchClientId: true,
                hasTwitchSecret: false,
                hasTwitchDecision: true,
                hasPlatformBot: false,
                hasSpotify: false,
                hasDiscord: false,
                hasYouTube: false,
                "https://bot.example"
            )
            .Steps.First(s => s.Key == "twitch_app");
        twitchDecided.Complete.Should().BeTrue();
    }

    [Fact]
    public void Build_is_not_complete_on_a_usable_client_id_alone_with_no_recorded_decision()
    {
        // THE BUG: on a completely empty database, the shipped public client id resolves (Ready/Status), but
        // nobody made a decision yet. Onboarding must stay NOT complete.
        SetupWizardDto wizard = SetupWizard.Build(
            hasTwitchClientId: true,
            hasTwitchSecret: false,
            hasTwitchDecision: false,
            hasPlatformBot: false,
            hasSpotify: false,
            hasDiscord: false,
            hasYouTube: false,
            "https://bot.example"
        );

        wizard.Complete.Should().BeFalse();
        wizard.Steps.Single(s => s.Key == "twitch_app").Complete.Should().BeFalse();
    }

    [Fact]
    public void Build_is_complete_once_a_decision_is_recorded_with_no_bot()
    {
        // Onboarding (Complete) is deployment-level configuration ONLY: a RECORDED decision makes it done, full
        // stop. The bot is per-channel work that happens after login — it must never gate onboarding completion,
        // even though the wizard's own "platform_bot" step still separately tracks its own live state.
        SetupWizardDto wizard = SetupWizard.Build(
            hasTwitchClientId: true,
            hasTwitchSecret: false,
            hasTwitchDecision: true,
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
            hasTwitchDecision: false,
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
            .Build(false, false, false, false, false, false, false, "https://bot.example/")
            .Steps.First(s => s.Key == "twitch_app");

        twitch.Instructions.Should().Contain(i => i.Contains("https://bot.example/api/v1/auth"));
        twitch.Instructions.Should().NotContain(i => i.Contains("bot.example//api"));
    }
}
