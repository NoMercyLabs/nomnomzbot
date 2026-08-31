// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.ChannelBasics
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.DeviceBotPoll
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.SetupStep
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.UpdateBasicsBody
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The first-run setup wizard's state-holder (frontend.md §4 — a plain holder, not a ViewModel). It owns
// the self-describing wizard (loaded from the backend), the per-step credential inputs, and the busy /
// error state, and runs the REAL onboarding against the chosen backend through the typed [SystemApi]:
//
//   load() → GET …/setup/wizard  (the steps + the wizard's own Complete gate, rendered verbatim)
//   saveCredentials(step) → PUT …/setup/credentials/{provider} → reload (the step flips to complete)
//   connectBot() → GET …/setup/bot/oauth-url → open it → poll …/setup/bot/status → reload
//   finish() → run the streamer OAuth via [onReadyToSignIn] → POST …/setup/complete
//
// Nothing is faked: a step is "complete" only when the backend's reloaded wizard says so — never an
// optimistic local flip. The flow advances to the streamer sign-in only once [SetupWizard.complete] is true
// (a platform's app credentials configured — never gated on the platform bot, which the wizard's own
// "platform_bot" step tracks separately), which the backend computes.
class SetupController(
    private val systemApi: SystemApi,
    private val connectLauncher: ConnectLauncher,
    // The secret-free bot device-login facade — connectBot() drives this exclusively (device code +
    // poll), never the redirect flow: a fresh self-host has no Twitch app configured yet, so the
    // redirect path (which needs a client secret) cannot work at this point in onboarding.
    private val botAuthApi: BotAuthApi,
    // The channel facade + per-channel settings facade — used only at finish() to persist the onboarding
    // "basics" (prefix / language / timezone) to the freshly-signed-in streamer's channel.
    private val channelsApi: ChannelsApi,
    private val channelSettingsApi: ChannelSettingsApi,
    // Hand off to the streamer OAuth once setup is ready. Returns true when the session was established
    // (the gate advances to the shell); false leaves the wizard up with [SetupError.SignIn] surfaced.
    private val onReadyToSignIn: suspend () -> Boolean,
) {
    private val _state: MutableStateFlow<SetupState> = MutableStateFlow(SetupState.Loading)

    /** The screen's render state: loading / steps (with field inputs + ready flag) / error. */
    val state: StateFlow<SetupState> = _state.asStateFlow()

    // The user-entered values per step, keyed by "<stepKey>.<fieldKey>". Held outside the rendered state so
    // a reload (which replaces the wizard) doesn't wipe in-progress input the user hasn't saved yet.
    private val fieldValues: MutableMap<String, String> = mutableMapOf()

    // The onboarding "basics" the user fills on the review step. Held outside the rendered state (like
    // [fieldValues]) so a reload doesn't wipe it; applied to the channel at finish(), once signed in.
    private var basics: SetupBasics = SetupBasics()

    /** Load the self-describing wizard + readiness and render its steps. */
    suspend fun load() {
        _state.value = SetupState.Loading
        reload(busy = null, error = null)
    }

    /** Edit one field's value (keyed by step + field), clearing any prior error. */
    fun onFieldChange(stepKey: String, fieldKey: String, value: String) {
        fieldValues[fieldKeyOf(stepKey, fieldKey)] = value
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(values = fieldValues.toMap(), error = null)
    }

    /**
     * Advance to the next step. Guarded by [SetupState.Steps.canAdvance] so a required step can't be skipped
     * before the backend confirms it complete; on the last backend step the next position is the review
     * step (still in range — see [SetupState.Steps.lastIndex]).
     */
    fun next() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        if (!current.canAdvance || current.currentStep >= current.lastIndex) return
        _state.value = current.copy(currentStep = current.currentStep + 1, error = null)
    }

    /** Move back to the previous step (no-op on the first), clearing any surfaced error. */
    fun back() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        if (current.currentStep <= 0) return
        _state.value = current.copy(currentStep = current.currentStep - 1, error = null)
    }

    /** The current value of a field (empty when untouched). */
    fun valueOf(stepKey: String, fieldKey: String): String = fieldValues[fieldKeyOf(stepKey, fieldKey)].orEmpty()

    /** Edit the onboarding basics (prefix / language / timezone) shown on the review step. */
    fun onBasicsChange(basics: SetupBasics) {
        this.basics = basics
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(basics = basics)
    }

    /**
     * Save a `save_credentials` step's inputs (Twitch / Spotify / YouTube / Discord), then reload so the
     * step flips to complete from the backend's re-read — never an optimistic flip.
     */
    suspend fun saveCredentials(step: SetupStep) {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = step.key, error = null)

        val clientId: String = valueOf(step.key, FIELD_CLIENT_ID).trim()
        val clientSecret: String = valueOf(step.key, FIELD_CLIENT_SECRET).trim()
        // Twitch logs in secret-free via the Device Code Flow (client id alone); the secret is an optional
        // enhancement. The other providers are confidential OAuth clients and still need both fields.
        val secretRequired: Boolean = step.key != STEP_TWITCH
        if (clientId.isEmpty() || (secretRequired && clientSecret.isEmpty())) {
            _state.value = current.copy(busy = null, error = SetupError.MissingFields(step.key))
            return
        }

        // Twitch is the one step with a shape of its own (secret-optional + a bot-username field); every other
        // `save_credentials` step — spotify/discord/youtube today, a future kick/twitter/… login platform's
        // app-credential step tomorrow — saves through the SAME generic provider call keyed by step.key, so
        // adding one is a backend wizard-step registration, never a new branch here.
        val result: ApiResult<Unit> =
            if (step.key == STEP_TWITCH)
                systemApi.saveTwitchCredentials(
                    clientId = clientId,
                    clientSecret = clientSecret,
                    botUsername = valueOf(step.key, FIELD_BOT_USERNAME).trim().ifEmpty { null },
                )
            else
                systemApi.saveCredentials(step.key, clientId, clientSecret)

        when (result) {
            is ApiResult.Failure -> _state.value = current.copy(busy = null, error = SetupError.Save(step.key, result.error.message))
            is ApiResult.Ok -> reload(busy = null, error = null)
        }
    }

    /**
     * Record the operator's one-click decision to use the shared NomNomzBot Twitch app instead of BYOC, then
     * reload so the step flips to complete from the backend's re-read — the same "never an optimistic flip"
     * contract [saveCredentials] follows.
     */
    suspend fun useSharedTwitchApp() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = STEP_TWITCH, error = null)

        when (val result: ApiResult<Unit> = systemApi.useSharedTwitchApp()) {
            is ApiResult.Failure ->
                _state.value = current.copy(busy = null, error = SetupError.Save(STEP_TWITCH, result.error.message))
            is ApiResult.Ok -> reload(busy = null, error = null)
        }
    }

    /**
     * Run the platform-bot authorization via the secret-free DEVICE CODE flow (CLAUDE.md: login is device
     * code, secret-free, shared public client by default) — never the redirect flow, which needs a
     * configured Twitch app the operator does not have yet at this point in onboarding. Mints a device
     * code, surfaces it on the step (user code + verification link), and polls until the operator
     * approves at twitch.tv/activate — then reloads so the step reflects the backend's re-read status.
     * Single-flight: a login already in progress is not restarted.
     */
    suspend fun connectBot() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        if (current.botDevice != null) return

        _state.value = current.copy(busy = STEP_PLATFORM_BOT, error = null)

        when (val start: ApiResult<DeviceCodeStart> = botAuthApi.startDeviceLogin()) {
            is ApiResult.Failure ->
                _state.value =
                    current.copy(
                        busy = null,
                        error = SetupError.Bot(start.error.message, unreachable = start.error.status == 0),
                    )
            is ApiResult.Ok -> {
                _state.value =
                    current.copy(
                        busy = STEP_PLATFORM_BOT,
                        error = null,
                        botDevice = BotDeviceState(userCode = start.value.userCode, verificationUri = start.value.verificationUri),
                    )
                pollBotDevice(start.value)
            }
        }
    }

    /** Abandon an in-flight bot device login (the operator closed the panel) without waiting further. */
    fun cancelBotDevice() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = null, botDevice = null)
    }

    // Poll the bot device endpoint on its own interval until the operator approves (→ reload so the step
    // reflects the backend's re-read, the same "never an optimistic flip" contract every other step
    // follows), declines, or the code expires. A transient poll failure is tolerated until the deadline
    // so a blip mid-approval doesn't abort the connect. The delay is a coroutine suspend, never a thread
    // block, and the loop bails immediately if [cancelBotDevice] cleared botDevice out from under it.
    private suspend fun pollBotDevice(start: DeviceCodeStart) {
        val intervalMs: Long = start.interval.coerceAtLeast(1).toLong() * 1000L
        val deadlineMs: Long = start.expiresIn.coerceAtLeast(1).toLong() * 1000L
        var elapsedMs: Long = 0

        while (elapsedMs < deadlineMs && (_state.value as? SetupState.Steps)?.botDevice != null) {
            delay(intervalMs)
            elapsedMs += intervalMs

            when (val poll: ApiResult<DeviceBotPoll> = botAuthApi.pollDeviceLogin(start.deviceCode)) {
                is ApiResult.Failure -> Unit // tolerate transient failures until the code's deadline.
                is ApiResult.Ok ->
                    when (poll.value.status) {
                        DEVICE_AUTHORIZED -> {
                            // The shared bot is vaulted server-side; re-read the authoritative status (no fakes).
                            reload(busy = null, error = null)
                            return
                        }
                        DEVICE_EXPIRED, DEVICE_DENIED, DEVICE_ERROR -> {
                            failBotDevice(poll.value.status)
                            return
                        }
                        else -> Unit // pending / slow_down — keep polling.
                    }
            }
        }
        // Timed out without approval — the code simply expired; surface it as such and stay retryable.
        if ((_state.value as? SetupState.Steps)?.botDevice != null) failBotDevice(DEVICE_EXPIRED)
    }

    // Drop the device panel and surface [reason] (a raw status token) through the same SetupError.Bot the
    // redirect path used — the screen wraps it in the localized "Bot authorization failed: %s" template, so
    // the raw token is never shown to the user standalone. The step stays retryable: busy clears and
    // botDevice is dropped, so the Authorize button reappears.
    private fun failBotDevice(reason: String) {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        _state.value = current.copy(busy = null, botDevice = null, error = SetupError.Bot(reason, unreachable = false))
    }

    /**
     * Setup is ready (Twitch app + platform bot configured): run the streamer OAuth, and on success mark
     * setup complete. The gate advances to the shell inside [onReadyToSignIn]; a failure surfaces
     * [SetupError.SignIn] and leaves the wizard up.
     */
    suspend fun finish() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return
        if (!current.ready) return
        _state.value = current.copy(busy = SIGNING_IN, error = null)

        val signedIn: Boolean = onReadyToSignIn()
        if (!signedIn) {
            _state.value = current.copy(busy = null, error = SetupError.SignIn)
            return
        }
        // The streamer session is live; finalize setup so the credential endpoints lock to admins.
        systemApi.completeSetup()
        // Persist the onboarding basics to the streamer's now-onboarded channel. Setup itself is already
        // finalized at this point (never blocked on this write), but a rejected/failed write is a real
        // failure the operator must see and can retry from — never swallowed. On success, S070 clears busy
        // with no error; on failure, busy clears and the real backend error message is surfaced instead —
        // the wizard stays on the review step (no further step exists to advance past).
        applyBasics()
    }

    // Resolve the signed-in streamer's channel and PUT the collected basics. A blank prefix falls back to the
    // conventional "!" so onboarding never persists an empty (match-everything) prefix; blank locale/timezone
    // are sent as null (leave unchanged).
    //
    // The bot-line marker (D5: "the bot types as the streamer's own account with a user-defined line prefix"
    // until a dedicated bot account connects) is a SEPARATE field from the command prefix above — it must
    // never be conflated with it. Mirrors the same "connected ⇒ marker is meaningless" rule the Settings
    // "Bot basics" tab enforces (SettingsScreen.kt BasicsForm): once the platform_bot step is complete, the
    // marker is left unchanged (null) rather than overwritten with whatever the user typed before connecting;
    // otherwise the trimmed value the user entered on the review step is persisted as the actual line prefix.
    //
    // S070: neither failure path here is silent — a channel that can't be resolved, or a rejected write,
    // surfaces the backend's real error message via [SetupError.Basics] rather than leaving the operator with
    // no feedback (or a state that only LOOKS like it succeeded).
    private suspend fun applyBasics() {
        val current: SetupState.Steps = _state.value as? SetupState.Steps ?: return

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = current.copy(busy = null, error = SetupError.Basics(result.error.message))
                    return
                }
                is ApiResult.Ok -> result.value
            }
        val prefix: String = basics.prefix.trim().ifEmpty { "!" }
        val platformBotConnected: Boolean = (_state.value as? SetupState.Steps)?.platformBotConnected == true
        val result: ApiResult<ChannelBasics> =
            channelSettingsApi.updateBasics(
                channel.id,
                UpdateBasicsBody(
                    prefix = prefix,
                    locale = basics.locale.trim().ifEmpty { null },
                    timezone = basics.timezone.trim().ifEmpty { null },
                    botLinePrefix = if (platformBotConnected) null else basics.botLinePrefix.trim(),
                ),
            )

        when (result) {
            is ApiResult.Failure -> _state.value = current.copy(busy = null, error = SetupError.Basics(result.error.message))
            is ApiResult.Ok -> _state.value = current.copy(busy = null, error = null)
        }
    }

    // Re-read the wizard + readiness from the backend and rebuild the steps state. Preserves the user's
    // in-progress field values (held in [fieldValues]) AND the current step index (so a save/reload — which
    // replaces the wizard — keeps the user on the step they were filling in, never bouncing back to step 1).
    private suspend fun reload(busy: String?, error: SetupError?) {
        val wizard: SetupWizard =
            when (val result: ApiResult<SetupWizard> = systemApi.wizard()) {
                is ApiResult.Failure -> {
                    _state.value = SetupState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The wizard's own [SetupWizardDto.Complete] (a client id present) IS the ready-to-sign-in gate —
        // never gated on the platform bot, which is per-channel work the "platform_bot" step tracks on its
        // own. No separate status() call is needed here; the wizard's re-read is already the source of truth.
        val ready: Boolean = wizard.complete

        // Keep the user on their current step across a reload; clamp in case the step count shrank.
        val priorStep: Int = (_state.value as? SetupState.Steps)?.currentStep ?: 0
        val lastIndex: Int = wizard.steps.size // backend steps + 1 review step ⇒ last valid index == size
        val currentStep: Int = priorStep.coerceIn(0, lastIndex)

        // The backend's re-read truth for whether a dedicated bot account is connected (never an optimistic
        // flip) — used at applyBasics() time to decide whether the bot-line prefix the user typed still
        // applies (D5), the same "connected ⇒ marker is meaningless" rule the Settings "Bot basics" tab uses.
        val platformBotConnected: Boolean = wizard.steps.find { it.key == STEP_PLATFORM_BOT }?.complete == true

        _state.value =
            SetupState.Steps(
                steps = wizard.steps,
                values = fieldValues.toMap(),
                ready = ready,
                busy = busy,
                error = error,
                currentStep = currentStep,
                basics = basics,
                platformBotConnected = platformBotConnected,
            )
    }

    private fun saveUnsupported(stepKey: String): ApiError =
        ApiError(
            status = 0,
            code = "UNSUPPORTED_STEP",
            message = "No save action for step $stepKey.",
        )

    private companion object {
        const val STEP_TWITCH: String = "twitch_app"
        const val STEP_PLATFORM_BOT: String = "platform_bot"
        const val STEP_SPOTIFY: String = "spotify"
        const val STEP_YOUTUBE: String = "youtube"
        const val STEP_DISCORD: String = "discord"

        const val FIELD_CLIENT_ID: String = "clientId"
        const val FIELD_CLIENT_SECRET: String = "clientSecret"
        const val FIELD_BOT_USERNAME: String = "botUsername"

        // A reserved busy token for the final streamer sign-in (distinct from any step key).
        const val SIGNING_IN: String = "__signing_in__"

        // The bot device-poll status tokens the backend returns (mirrors IntegrationsController's bot
        // device flow — the same backend endpoint, same vocabulary).
        const val DEVICE_AUTHORIZED: String = "authorized"
        const val DEVICE_EXPIRED: String = "expired"
        const val DEVICE_DENIED: String = "denied"
        const val DEVICE_ERROR: String = "error"

        fun fieldKeyOf(stepKey: String, fieldKey: String): String = "$stepKey.$fieldKey"
    }
}

/** An in-flight bot device login: the user code to show + the verification link to open/copy. */
data class BotDeviceState(val userCode: String, val verificationUri: String)

/** The setup wizard's render state. */
sealed interface SetupState {
    data object Loading : SetupState

    /**
     * The wizard's steps rendered verbatim from the backend, plus the per-field [values] the user has
     * entered, the [ready] gate (true ⇒ the streamer sign-in is enabled), the [busy] step key (null when
     * idle; the reserved sign-in token while signing in), the current [error], and [currentStep] — the
     * 0-based position in the multi-step flow. The flow is the backend [steps] followed by one trailing
     * **review** step, so the valid index range is `0..steps.size` (the last index, [reviewIndex], is the
     * review). The UI shows exactly one panel per index; this is the only "which screen" source.
     */
    data class Steps(
        val steps: List<SetupStep>,
        val values: Map<String, String>,
        val ready: Boolean,
        val busy: String?,
        val error: SetupError?,
        val currentStep: Int = 0,
        val basics: SetupBasics = SetupBasics(),
        // The in-flight bot device login (user code + verification link) while [connectBot] polls; null
        // when no bot login is in progress.
        val botDevice: BotDeviceState? = null,
        // The backend's re-read truth (never an optimistic flip) for whether a dedicated bot account is
        // connected — the "platform_bot" step's [SetupStep.complete]. Gates whether the review step's
        // bot-line-prefix field is meaningful (D5): once a dedicated bot is connected it types as itself
        // and the marker no longer applies.
        val platformBotConnected: Boolean = false,
    ) : SetupState {
        /** The index of the trailing review/finish step (one past the last backend step). */
        val reviewIndex: Int get() = steps.size

        /** The last valid step index — equals [reviewIndex]. */
        val lastIndex: Int get() = reviewIndex

        /** True when the current position is the review/finish step rather than a backend step. */
        val onReviewStep: Boolean get() = currentStep >= reviewIndex

        /** The backend step at the current position, or null when on the review step. */
        val currentBackendStep: SetupStep? get() = steps.getOrNull(currentStep)

        /**
         * Whether **Next** is allowed from the current step. A backend step advances only once it is
         * `complete` (the backend's re-read truth — never an optimistic flip) or it is optional; the review
         * step never "advances" (its Next is the finish action, gated separately by [ready]).
         */
        val canAdvance: Boolean
            get() {
                val step: SetupStep = currentBackendStep ?: return false
                return step.complete || !step.required
            }
    }

    data class Error(val detail: String) : SetupState
}

/**
 * The onboarding "basics" a new streamer fills on the review step: the command [prefix] (defaults to the
 * conventional "!"), the bot's default [locale], the streamer's [timezone], and the [botLinePrefix] (D5) —
 * the marker prepended to bot-typed lines while the bot posts through the streamer's own account (blank ⇒
 * no marker). Applied to the channel at finish() once signed in; [botLinePrefix] is ignored (left unchanged)
 * once a dedicated bot account is connected, since the marker no longer means anything at that point.
 */
data class SetupBasics(
    val prefix: String = "!",
    val locale: String = "",
    val timezone: String = "",
    val botLinePrefix: String = "",
)

/** Why a setup action failed — mapped to a localized message in the screen. */
sealed interface SetupError {
    /** A required credential field was left blank for [stepKey]. */
    data class MissingFields(val stepKey: String) : SetupError

    /** Saving [stepKey]'s credentials failed. */
    data class Save(val stepKey: String, val detail: String) : SetupError

    /** The platform-bot authorization failed. */
    data class Bot(val detail: String, val unreachable: Boolean = false) : SetupError

    /** The final streamer sign-in failed. */
    data object SignIn : SetupError

    /** Persisting the review step's onboarding basics (prefix/locale/timezone/bot-line-prefix) failed. */
    data class Basics(val detail: String) : SetupError
}
