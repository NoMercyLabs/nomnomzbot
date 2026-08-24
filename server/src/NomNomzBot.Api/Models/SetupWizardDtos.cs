// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Models;

/// <summary>
/// The self-describing first-time-setup contract a dashboard renders the onboarding flow from: an ordered list of
/// steps plus an overall <see cref="Complete"/> flag. Each step carries everything the UI needs — copy, the exact
/// redirect URI to register, the API call that satisfies it, the input fields, and its live completion state — so
/// the onboarding pages need no hardcoded knowledge of the steps.
/// </summary>
public sealed record SetupWizardDto(bool Complete, IReadOnlyList<SetupStepDto> Steps);

/// <summary>One onboarding step: presentation + live state + the action and fields that complete it.</summary>
public sealed record SetupStepDto(
    string Key,
    string Title,
    string Description,
    bool Required,
    bool Complete,
    string Status,
    IReadOnlyList<string> Instructions,
    SetupActionDto Action,
    IReadOnlyList<SetupFieldDto> Fields,
    // The one-click "use the shared app" affordance — present only on a login-platform step that ships a
    // public default client id (today just twitch_app). Null on every step with no shared-app alternative
    // (the bot step, and the confidential-client optional integrations). Choosing this records the SAME kind
    // of deliberate decision as saving BYOC credentials via <see cref="Action"/> — either one completes the
    // step; this is never a fallback the step completes on its own.
    SetupActionDto? UseSharedAction = null
);

/// <summary>
/// How a UI completes a step. <c>Type</c> is <c>save_credentials</c> (POST/PUT the <see cref="SetupStepDto.Fields"/>
/// to <c>Endpoint</c>) or <c>oauth_redirect</c> (GET <c>Endpoint</c> for a URL to send the browser to, then poll
/// <c>PollEndpoint</c> for completion).
/// </summary>
public sealed record SetupActionDto(
    string Type,
    string Method,
    string Endpoint,
    string? PollEndpoint
);

/// <summary>One input the user fills in for a step (key, label, control type, required flag, help text).</summary>
public sealed record SetupFieldDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? Help
);

/// <summary>
/// Builds the onboarding wizard contract from the live setup state + the public base URL. Pure (no I/O) so the
/// step structure, the exact redirect URIs, and the completion mapping are unit-tested directly; the controller
/// just supplies the state.
/// </summary>
public static class SetupWizard
{
    public static SetupWizardDto Build(
        bool hasTwitchClientId,
        bool hasTwitchSecret,
        bool hasTwitchDecision,
        bool hasPlatformBot,
        bool hasSpotify,
        bool hasDiscord,
        bool hasYouTube,
        string baseUrl
    )
    {
        string root = baseUrl.TrimEnd('/');
        List<SetupStepDto> steps =
        [
            new(
                "twitch_app",
                "Connect your Twitch application",
                "The bot talks to Twitch through a Twitch app. Bring your own Client ID (encouraged — a Client Secret is optional and only adds one-tap redirect sign-in), or use the shared NomNomzBot app in one click.",
                Required: true,
                // Complete only once the operator made a DELIBERATE, RECORDED decision — BYOC saved, or the
                // shared app explicitly chosen via UseSharedAction below. A client id merely being READY
                // (Status below — the shipped config default resolving on its own) is NOT a decision and must
                // never complete this step on its own; that was the onboarding-skip bug.
                Complete: hasTwitchDecision,
                // Status still reports what's USABLE right now (any source, including the shipped default) —
                // this is what the "ready_device" copy and the shared-app affordance's availability key off.
                Status: !hasTwitchClientId ? "missing"
                    : hasTwitchSecret ? "ready_redirect"
                    : "ready_device",
                Instructions:
                [
                    "Bring your own app (encouraged): open the Twitch Developer Console at https://dev.twitch.tv/console/apps and register a new application.",
                    $"Set the OAuth Redirect URL to exactly: {root}/api/v1/auth/twitch/callback",
                    "Choose the \"Chat Bot\" category, create it, then copy the Client ID.",
                    "A Client Secret is optional — the bot signs in with the secret-free device-code flow without one. Add it only if you want the smoother one-tap redirect sign-in.",
                    "Prefer not to register your own app? Use the shared NomNomzBot app instead — one click, no fields.",
                ],
                Action: new(
                    "save_credentials",
                    "PUT",
                    "/api/v1/system/setup/credentials/twitch",
                    null
                ),
                Fields:
                [
                    new(
                        "clientId",
                        "Client ID",
                        "text",
                        true,
                        "From your Twitch app's settings page."
                    ),
                    new(
                        "clientSecret",
                        "Client Secret (optional)",
                        "password",
                        false,
                        "Optional — only needed for one-tap redirect sign-in. Generated on the Twitch app page, shown only once."
                    ),
                ],
                // The one-click alternative to BYOC: records the operator's choice to run on the shared public
                // client id (already usable via device-code — Status above) without registering their own app.
                UseSharedAction: new(
                    "use_shared",
                    "POST",
                    "/api/v1/system/setup/credentials/twitch/use-shared",
                    null
                )
            ),
            new(
                "platform_bot",
                "Authorize the bot account",
                "Sign in as the Twitch account the bot posts chat from, and approve its chat permissions.",
                Required: true,
                Complete: hasPlatformBot,
                Status: hasPlatformBot ? "connected" : "disconnected",
                Instructions:
                [
                    "Log into Twitch as the bot's own account in this browser first.",
                    "Start the authorization, then approve the requested chat scopes.",
                ],
                Action: new(
                    "oauth_redirect",
                    "GET",
                    "/api/v1/system/setup/bot/oauth-url",
                    "/api/v1/system/setup/bot/status"
                ),
                Fields: []
            ),
            new(
                "spotify",
                "Spotify (optional)",
                "Connect a Spotify app to enable song requests.",
                Required: false,
                Complete: hasSpotify,
                Status: hasSpotify ? "configured" : "not_configured",
                Instructions:
                [
                    "Create an app at https://developer.spotify.com/dashboard.",
                    $"Add the Redirect URI: {root}/api/v1/integrations/spotify/callback",
                    "Copy the Client ID and Client Secret.",
                ],
                Action: new(
                    "save_credentials",
                    "PUT",
                    "/api/v1/system/setup/credentials/spotify",
                    null
                ),
                Fields:
                [
                    new("clientId", "Client ID", "text", true, null),
                    new("clientSecret", "Client Secret", "password", true, null),
                ]
            ),
            new(
                "discord",
                "Discord (optional)",
                "Connect a Discord app for notifications and guild sync.",
                Required: false,
                Complete: hasDiscord,
                Status: hasDiscord ? "configured" : "not_configured",
                Instructions:
                [
                    "Create an application at https://discord.com/developers/applications.",
                    $"Add the OAuth2 Redirect: {root}/api/v1/integrations/discord/callback",
                    "Copy the Client ID and Client Secret from the OAuth2 page.",
                ],
                Action: new(
                    "save_credentials",
                    "PUT",
                    "/api/v1/system/setup/credentials/discord",
                    null
                ),
                Fields:
                [
                    new("clientId", "Client ID", "text", true, null),
                    new("clientSecret", "Client Secret", "password", true, null),
                ]
            ),
            new(
                "youtube",
                "YouTube (optional)",
                "Connect a Google app to enable the YouTube music provider.",
                Required: false,
                Complete: hasYouTube,
                Status: hasYouTube ? "configured" : "not_configured",
                Instructions:
                [
                    "Create OAuth credentials at https://console.cloud.google.com/apis/credentials.",
                    $"Add the Authorized redirect URI: {root}/api/v1/integrations/youtube/callback",
                    "Copy the Client ID and Client Secret.",
                ],
                Action: new(
                    "save_credentials",
                    "PUT",
                    "/api/v1/system/setup/credentials/youtube",
                    null
                ),
                Fields:
                [
                    new("clientId", "Client ID", "text", true, null),
                    new("clientSecret", "Client Secret", "password", true, null),
                ]
            ),
        ];

        // Complete = onboarding done = at least one platform's app has a RECORDED DECISION (today just
        // Twitch's — BYOC or explicit shared-app choice). Mirrors SystemController.GetStatus's
        // OnboardingComplete exactly; the platform bot is NOT part of this — it's per-channel work the
        // wizard's own "platform_bot" step still tracks (see its own Complete/Status above), never a reason
        // to keep onboarding "not done".
        return new(hasTwitchDecision, steps);
    }
}
