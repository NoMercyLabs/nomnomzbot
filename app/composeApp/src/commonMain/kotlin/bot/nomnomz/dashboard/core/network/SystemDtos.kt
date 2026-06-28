// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// Hand-authored mirrors of the backend first-run setup contract (SystemController). These move into the
// committed OpenAPI-generated layer (core/network/generated) when the generator task lands; the SystemApi
// facade keeps the same surface, so callers don't change. Every call here is ANONYMOUS — the system must
// be configurable before any user can sign in, so no bearer is attached for these endpoints.

/**
 * System readiness (`GET /api/v1/system/status` → StatusResponseDto<SystemStatusDto>). [ready] is the
 * single gate the onboarding flow routes on: false ⇒ the Twitch app (and/or platform bot) is not yet
 * configured, so the wizard must run before any Twitch OAuth can start.
 */
@Serializable
data class SystemStatus(
    val ready: Boolean,
    val checks: SystemChecks,
)

/** Per-area readiness checks the status carries; the wizard renders detail from these. */
@Serializable
data class SystemChecks(
    val twitchApp: SystemCheck,
    val platformBot: SystemCheck,
    val spotify: SystemCheck? = null,
    val discord: SystemCheck? = null,
)

/**
 * One readiness check. [ready] means the area is USABLE NOW; [ok] means the FULL credential set is present.
 * For Twitch the two diverge: a client id alone makes the bot fully functional via the secret-free device-code
 * flow ([ready] = true), while a client secret is the pure enhancement that also unlocks the one-tap redirect
 * sign-in ([ok] = true). For every other area they coincide. Onboarding routes off both: [ready] gates whether
 * the flow can start; [ok] picks the smoother redirect login over the device-code dance.
 */
@Serializable
data class SystemCheck(
    val ok: Boolean,
    val ready: Boolean = false,
    val status: String,
    val detail: String? = null,
)

/**
 * The self-describing first-time-setup contract (`GET /api/v1/system/setup/wizard` →
 * StatusResponseDto<SetupWizardDto>). The wizard UI renders entirely from this — the ordered [steps],
 * each carrying its own copy, instructions, the exact redirect URI to register, the action that
 * completes it, and its input fields — so the screen needs no hardcoded knowledge of the steps.
 */
@Serializable
data class SetupWizard(
    val complete: Boolean,
    val steps: List<SetupStep> = emptyList(),
)

/** One onboarding step: presentation + live completion state + the action and fields that complete it. */
@Serializable
data class SetupStep(
    val key: String,
    val title: String,
    val description: String,
    val required: Boolean,
    val complete: Boolean,
    val status: String,
    val instructions: List<String> = emptyList(),
    val action: SetupAction,
    val fields: List<SetupField> = emptyList(),
)

/**
 * How the UI completes a step. [type] is `save_credentials` (PUT the [SetupStep.fields] to [endpoint]) or
 * `oauth_redirect` (GET [endpoint] for a URL to open, then poll [pollEndpoint] for completion).
 */
@Serializable
data class SetupAction(
    val type: String,
    val method: String,
    val endpoint: String,
    val pollEndpoint: String? = null,
)

/** One input the user fills in for a step: a stable [key], a [label], a control [type], and help. */
@Serializable
data class SetupField(
    val key: String,
    val label: String,
    val type: String,
    val required: Boolean,
    val help: String? = null,
)

/** The credential body for the Twitch app step (`PUT …/setup/credentials/twitch`). */
@Serializable
data class SaveTwitchCredentialsBody(
    val clientId: String,
    val clientSecret: String,
    val botUsername: String? = null,
)

/**
 * The credential body the generic provider steps share (`PUT …/setup/credentials/{spotify|youtube|discord}`).
 */
@Serializable
data class SaveCredentialsBody(
    val clientId: String,
    val clientSecret: String,
)

/** The bot authorize URL the setup bot step opens (`GET …/setup/bot/oauth-url` → `{ oauthUrl }`). */
@Serializable
data class BotOAuthUrl(
    val oauthUrl: String,
)

/** One entry in the pronoun catalogue (`GET /api/v1/system/pronouns` or `GET /api/v1/pronouns/catalog`). */
@Serializable
data class PronounOption(
    val id: Int,
    val name: String,
    val subject: String,
    val `object`: String,
    val key: String? = null,
)

/** Viewer's current pronoun state (`GET /api/v1/pronouns/me`). */
@Serializable
data class UserPronounResponse(
    val pronounId: Int? = null,
    val pronounName: String? = null,
    val pronounBadge: String? = null,
    val altPronounId: Int? = null,
    val altPronounName: String? = null,
    val manualOverride: Boolean = false,
)

/** Body for `PUT /api/v1/pronouns/me`. `0` clears, `null` leaves unchanged. */
@Serializable
data class SetPronounBody(
    val pronounId: Int? = null,
    val altPronounId: Int? = null,
    val manualOverride: Boolean? = null,
)
