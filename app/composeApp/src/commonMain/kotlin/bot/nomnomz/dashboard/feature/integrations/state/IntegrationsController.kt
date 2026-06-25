// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.integrations.state

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.DeviceBotPoll
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.IntegrationStatus
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.MissingScope
import bot.nomnomz.dashboard.core.network.MissingScopes
import bot.nomnomz.dashboard.core.network.OAuthStart
import bot.nomnomz.dashboard.core.network.ScopeRegrantStart
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_connect_failed
import nomnomzbot.composeapp.generated.resources.feedback_credentials_saved
import nomnomzbot.composeapp.generated.resources.feedback_disconnect_failed
import nomnomzbot.composeapp.generated.resources.feedback_disconnected
import org.jetbrains.compose.resources.StringResource

// The integrations/onboarding screen's state-holder (frontend.md §4 — a plain holder, not a ViewModel).
// It owns the bot-account connect and the Spotify / YouTube / Discord connects, all against the REAL
// backend. The token always lands SERVER-SIDE, so every connect is: open the backend-issued authorize
// URL → wait for / return from the browser → re-read the authoritative status. Nothing is mocked.
//
// It also owns the streamer-token scope health: which Twitch permissions a feature needs that the token
// is missing (read from /twitch/diagnostics/missing-scopes), and the ONE-CLICK additive re-grant — a
// secret-free Device Code Flow requesting (granted ∪ missing). The streamer approves the widened grant at
// twitch.tv/activate; the controller polls the normal streamer device poll until authorized, then re-reads
// the gaps (which clear server-side). No manual re-auth instructions, no back-fill.
class IntegrationsController(
    private val sessionStore: SessionStore,
    private val channelsApi: ChannelsApi,
    private val botAuthApi: BotAuthApi,
    private val integrationsApi: IntegrationsApi,
    private val connectLauncher: ConnectLauncher,
    private val diagnosticsApi: TwitchDiagnosticsApi,
    private val authApi: AuthApi,
    // Read to choose the bot connect method off the SAME secret-present signal the streamer login uses
    // (twitchApp.ok): a configured client secret unlocks the one-tap redirect bot flow; without one, the
    // bot connects via the secret-free device-code flow. A Twitch client secret is optional throughout.
    private val systemApi: SystemApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<IntegrationsState> =
        MutableStateFlow(IntegrationsState.Loading)

    /** The screen's render state: loading / ready (with per-provider rows) / error. */
    val state: StateFlow<IntegrationsState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load the bot + integration statuses. */
    suspend fun load() {
        _state.value = IntegrationsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = IntegrationsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        refresh()
    }

    /** Re-read every status and rebuild the ready state. Used after any connect/disconnect returns. */
    suspend fun refresh() {
        val id: String = channelId ?: return

        val bot: BotConnection =
            when (val result: ApiResult<BotStatus> = botAuthApi.status()) {
                is ApiResult.Ok -> BotConnection(result.value.connected, result.value.displayName ?: result.value.login)
                is ApiResult.Failure -> BotConnection(connected = false, accountName = null)
            }

        val providers: List<ProviderConnection> =
            when (val result: ApiResult<List<IntegrationStatus>> = integrationsApi.status(id)) {
                is ApiResult.Ok -> result.value.map { it.toProviderConnection() }
                is ApiResult.Failure -> emptyList()
            }

        // The streamer-token scope gaps. A failure (e.g. no Twitch connection yet) is a non-event — render
        // an empty list, never an error that hides the whole screen.
        val missingScopes: List<MissingScope> =
            when (val result: ApiResult<MissingScopes> = diagnosticsApi.missingScopes()) {
                is ApiResult.Ok -> result.value.scopes
                is ApiResult.Failure -> emptyList()
            }

        // The per-provider APP-CREDENTIAL (client) registration signal, read off the same system status the
        // bot-connect method picks from. This drives the register-client-then-login flow: a provider whose
        // client is NOT yet registered must collect the operator's own BYOC credentials before OAuth can run.
        // A status failure leaves it null — every provider then reads "unknown", so the flow safely routes
        // through the credential card rather than launching an OAuth that can't succeed.
        val checks: SystemChecks? =
            when (val result: ApiResult<SystemStatus> = systemApi.status()) {
                is ApiResult.Ok -> result.value.checks
                is ApiResult.Failure -> null
            }

        // Preserve any in-flight panels across a refresh (their poll loops refresh mid-flow).
        val ready: IntegrationsState.Ready? = _state.value as? IntegrationsState.Ready
        _state.value =
            IntegrationsState.Ready(
                bot = bot,
                providers = providers,
                missingScopes = missingScopes,
                checks = checks,
                busy = null,
                regrant = ready?.regrant,
                botDevice = ready?.botDevice,
            )
    }

    /**
     * Whether the app CLIENT (BYOC credentials) for [provider] is registered, so the connect can go straight
     * to OAuth. `true` = registered (proceed to OAuth); `false` = not registered (collect credentials first);
     * `null` = UNKNOWN, because the backend system status exposes no client check for this provider yet (e.g.
     * YouTube — see the SystemChecks gap note). The caller treats `null` like `false` (route through the
     * credential card) so a connect is never launched against a client the bot can't prove is configured.
     */
    fun clientRegistered(provider: String): Boolean? {
        val checks: SystemChecks = (_state.value as? IntegrationsState.Ready)?.checks ?: return null
        val check: SystemCheck? =
            when (provider.lowercase()) {
                "spotify" -> checks.spotify
                "discord" -> checks.discord
                // BACKEND GAP: SystemChecks carries no `youtube` field, so YouTube's client registration can't
                // be read. Until the backend adds it, YouTube reports unknown and always routes through the
                // credential card (a save is idempotent, so re-registering an already-set client is harmless).
                else -> null
            }
        return check?.ok
    }

    /**
     * The exact OAuth redirect URL the operator pastes into the provider's developer console when registering
     * their BYOC app — the generic IntegrationOAuthController callback (`/api/v1/integrations/{provider}/callback`),
     * rooted at the ACTIVE backend base so the address is correct for THIS connection (self-host localhost vs a
     * remote operator URL). Null when no backend is active. Discord shares the same callback shape.
     */
    fun integrationRedirectUrl(provider: String): String? {
        val base: String = sessionStore.baseUrl()?.trimEnd('/') ?: return null
        return "$base/api/v1/integrations/${provider.lowercase()}/callback"
    }

    /**
     * Register the operator's own BYOC app credentials for [provider] (Spotify/YouTube/Discord) through the
     * wizard's per-provider credential endpoint, then re-read status so [clientRegistered] reflects the
     * backend. The client id is required (a blank id is a client-side guard); the secret rides as typed. On
     * success the feedback host announces it and the caller proceeds to OAuth; a failure surfaces on the host
     * and leaves the screen on the credential step. Returns whether the save succeeded.
     */
    suspend fun saveProviderCredentials(
        provider: String,
        clientId: String,
        clientSecret: String,
    ): Boolean {
        val id: String = clientId.trim()
        if (id.isEmpty()) return false

        val result: ApiResult<Unit> =
            when (provider.lowercase()) {
                "spotify" -> systemApi.saveSpotifyCredentials(id, clientSecret.trim())
                "youtube" -> systemApi.saveYouTubeCredentials(id, clientSecret.trim())
                "discord" -> systemApi.saveDiscordCredentials(id, clientSecret.trim())
                else -> return false
            }

        return when (result) {
            is ApiResult.Failure -> {
                feedback.error(Res.string.feedback_connect_failed, result.error.message)
                false
            }
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_credentials_saved)
                // Re-read status so the registered signal reflects the backend, not an optimistic flip.
                refresh()
                true
            }
        }
    }

    /**
     * Connect the platform-shared bot account, picking the method off the SAME secret-present signal the
     * streamer login uses (frontend.md §6 — a Twitch client secret is OPTIONAL). With a secret configured
     * (twitchApp.ok) the one-tap redirect bot flow runs — a clean tap → Twitch → loopback. Without one (the
     * shared public client / BYOC client id), only the device-code flow can authorize the bot, so it is used:
     * the operator approves a short user code at twitch.tv/activate and the controller polls until connected.
     * A failed readiness probe falls back to the redirect attempt, which surfaces a clean backend error.
     */
    suspend fun connectBot() {
        val redirectAvailable: Boolean =
            when (val status: ApiResult<SystemStatus> = systemApi.status()) {
                is ApiResult.Ok -> status.value.checks.twitchApp.ok
                is ApiResult.Failure -> false // no secret signal ⇒ take the always-available device path
            }

        if (redirectAvailable) connectBotViaRedirect() else connectBotViaDevice()
    }

    /** The one-tap redirect bot connect (a client secret is configured): open the authorize URL, then refresh. */
    private suspend fun connectBotViaRedirect() {
        withBusy(BusyTarget.Bot) {
            connectLauncher.awaitConnect { redirect ->
                botAuthApi.start(redirect).mapToAuthorizeUrl()
            }
        }
    }

    /**
     * The secret-free bot connect: mint a device code, surface it (the screen opens twitch.tv/activate), and
     * poll until the operator approves (→ the shared bot is connected + vaulted server-side, panel dismissed),
     * declines, or the code expires. Single-flight: never start a second device login while one is in flight.
     */
    suspend fun connectBotViaDevice() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        if (ready.botDevice != null) return // a bot device login is already in progress.

        when (val start: ApiResult<DeviceCodeStart> = botAuthApi.startDeviceLogin()) {
            is ApiResult.Failure -> feedback.error(Res.string.feedback_connect_failed, start.error.message)
            is ApiResult.Ok -> {
                _state.value =
                    ready.copy(
                        botDevice = BotDeviceState(
                            userCode = start.value.userCode,
                            verificationUri = start.value.verificationUri,
                        )
                    )
                pollBotDevice(start.value)
            }
        }
    }

    /** Dismiss the bot device-login panel without waiting (the user closed it). */
    fun cancelBotDevice() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(botDevice = null)
    }

    /**
     * Poll the bot device endpoint on its interval until the operator approves (→ refresh + dismiss the panel),
     * declines, or the code expires. A transient poll failure is tolerated until the deadline so a blip
     * mid-approval doesn't abort the connect. The delay is a coroutine suspend, never a thread block.
     */
    private suspend fun pollBotDevice(start: DeviceCodeStart) {
        val intervalMs: Long = start.interval.coerceAtLeast(1).toLong() * 1000L
        val deadlineMs: Long = start.expiresIn.coerceAtLeast(1).toLong() * 1000L
        var elapsedMs: Long = 0

        while (elapsedMs < deadlineMs && (_state.value as? IntegrationsState.Ready)?.botDevice != null) {
            delay(intervalMs)
            elapsedMs += intervalMs

            when (val poll: ApiResult<DeviceBotPoll> = botAuthApi.pollDeviceLogin(start.deviceCode)) {
                is ApiResult.Failure -> Unit // tolerate transient failures until the code's deadline.
                is ApiResult.Ok ->
                    when (poll.value.status) {
                        DEVICE_AUTHORIZED -> {
                            // The shared bot is vaulted server-side; re-read the authoritative status (no fakes).
                            cancelBotDevice()
                            refresh()
                            return
                        }
                        DEVICE_EXPIRED, DEVICE_DENIED, DEVICE_ERROR -> {
                            cancelBotDevice()
                            feedback.error(Res.string.feedback_connect_failed, poll.value.status)
                            return
                        }
                        else -> Unit // pending / slow_down — keep polling.
                    }
            }
        }
        // Timed out without approval — drop the panel; the bot is simply still disconnected.
        cancelBotDevice()
    }

    /** Run a Spotify/YouTube connect for [provider] with [scopeSetKey], then refresh. */
    suspend fun connectProvider(provider: String, scopeSetKey: String) {
        val id: String = channelId ?: return
        withBusy(BusyTarget.Provider(provider)) {
            connectLauncher.awaitConnect { redirect ->
                integrationsApi
                    .startGenericConnect(
                        channelId = id,
                        provider = provider,
                        scopeSetKey = scopeSetKey,
                        returnUrl = redirect.ifBlank { null },
                    )
                    .mapToAuthorizeUrl()
            }
        }
    }

    /** Open the Discord connect URL directly (no loopback signal — see class note), then refresh. */
    suspend fun connectDiscord() {
        val id: String = channelId ?: return
        val base: String = sessionStore.baseUrl() ?: return
        withBusy(BusyTarget.Provider("discord")) {
            connectLauncher.awaitConnect { _ ->
                ApiResult.Ok(integrationsApi.discordStartUrl(base, id))
            }
        }
    }

    /** Disconnect a provider (spotify/youtube/discord), then refresh. Announces the outcome on the frame. */
    suspend fun disconnect(provider: String) {
        val id: String = channelId ?: return
        withBusy(
            target = BusyTarget.Provider(provider),
            successMessage = Res.string.feedback_disconnected,
            failureMessage = Res.string.feedback_disconnect_failed,
        ) {
            if (provider.equals("discord", ignoreCase = true)) integrationsApi.disconnectDiscord(id)
            else integrationsApi.disconnectGeneric(id, provider)
        }
    }

    /**
     * Disconnect the platform-shared bot account (admin-only), then refresh so the bot card reflects it. To
     * CHANGE the bot, the operator disconnects here then connects the new account via [connectBot].
     */
    suspend fun disconnectBot() {
        withBusy(
            target = BusyTarget.Bot,
            successMessage = Res.string.feedback_disconnected,
            failureMessage = Res.string.feedback_disconnect_failed,
        ) {
            botAuthApi.disconnect()
        }
    }

    /**
     * Start the one-click additive scope re-grant. The backend mints a Device Code Flow handle requesting
     * (granted ∪ missing); we surface the user code + verification URL (the screen opens it) and poll the
     * normal streamer device poll until the operator approves at twitch.tv/activate — on approval the widened
     * grant reconciles server-side, so we re-read the gaps (they clear) and dismiss the panel. A failure to
     * START (e.g. nothing missing) leaves the screen unchanged; the poll loop tolerates the code expiring.
     */
    suspend fun regrantScopes() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        if (ready.regrant != null) return // single-flight: a re-grant is already in progress.

        when (val start: ApiResult<ScopeRegrantStart> = diagnosticsApi.startRegrant()) {
            is ApiResult.Failure -> return // e.g. NO_MISSING_SCOPES / no connection — nothing to grant.
            is ApiResult.Ok -> {
                _state.value =
                    ready.copy(
                        regrant = RegrantState(
                            userCode = start.value.userCode,
                            verificationUri = start.value.verificationUri,
                        )
                    )
                pollRegrant(start.value)
            }
        }
    }

    /** Dismiss the re-grant panel without waiting (the user closed it); the next refresh re-reads the gaps. */
    fun cancelRegrant() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(regrant = null)
    }

    /**
     * Poll the streamer device endpoint on its interval until the operator approves (→ refresh + dismiss the
     * panel), declines, or the code expires. A transient poll failure is tolerated until the deadline so a
     * blip mid-approval doesn't abort the re-grant.
     */
    private suspend fun pollRegrant(start: ScopeRegrantStart) {
        val intervalMs: Long = start.interval.coerceAtLeast(1).toLong() * 1000L
        val deadlineMs: Long = start.expiresIn.coerceAtLeast(1).toLong() * 1000L
        var elapsedMs: Long = 0

        while (elapsedMs < deadlineMs && (_state.value as? IntegrationsState.Ready)?.regrant != null) {
            delay(intervalMs)
            elapsedMs += intervalMs

            when (val poll: ApiResult<DeviceLoginPoll> = authApi.pollDeviceLogin(start.deviceCode)) {
                is ApiResult.Failure -> Unit // tolerate transient failures until the code's deadline.
                is ApiResult.Ok ->
                    when (poll.value.status) {
                        DEVICE_AUTHORIZED -> {
                            // The widened grant is vaulted + reconciled server-side; re-read the now-cleared gaps.
                            cancelRegrant()
                            refresh()
                            return
                        }
                        DEVICE_EXPIRED, DEVICE_DENIED, DEVICE_ERROR -> {
                            cancelRegrant()
                            return
                        }
                        else -> Unit // pending / slow_down — keep polling.
                    }
            }
        }
        // Timed out without approval — drop the panel; the gaps remain and can be re-granted again.
        cancelRegrant()
    }

    /**
     * Mark a target busy, run [action], then re-read status regardless of outcome (the authoritative
     * connected state always comes from the backend, never an optimistic local flip — no fakes). On the
     * frame: a failed action emits [failureMessage] (carrying the backend's error detail); a successful one
     * emits [successMessage] when given. Connect flows pass a null [successMessage] — their success is
     * confirmed by the post-redirect return on web (announced from main()) / the re-read status on desktop —
     * so they never claim "connected" before the backend says so.
     */
    private suspend fun withBusy(
        target: BusyTarget,
        successMessage: StringResource? = null,
        failureMessage: StringResource = Res.string.feedback_connect_failed,
        action: suspend () -> ApiResult<*>,
    ) {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(busy = target)
        when (val result: ApiResult<*> = action()) {
            is ApiResult.Ok -> successMessage?.let { feedback.success(it) }
            is ApiResult.Failure -> feedback.error(failureMessage, result.error.message)
        }
        refresh()
    }

    private fun IntegrationStatus.toProviderConnection(): ProviderConnection =
        ProviderConnection(
            provider = provider,
            connected = connected,
            accountName = accountName,
            needsReauth = needsReauth,
        )

    /** Project an [OAuthStart] result to just its authorize URL for the launcher to open. */
    private fun ApiResult<OAuthStart>.mapToAuthorizeUrl(): ApiResult<String> =
        when (this) {
            is ApiResult.Failure -> ApiResult.Failure(error)
            is ApiResult.Ok -> ApiResult.Ok(value.authorizeUrl)
        }

    private companion object {
        // The streamer device-poll statuses (server-side DeviceLoginStatus wire strings).
        const val DEVICE_AUTHORIZED = "authorized"
        const val DEVICE_EXPIRED = "expired"
        const val DEVICE_DENIED = "denied"
        const val DEVICE_ERROR = "error"
    }
}

/** The integrations screen render state. */
sealed interface IntegrationsState {
    data object Loading : IntegrationsState

    data class Ready(
        val bot: BotConnection,
        val providers: List<ProviderConnection>,
        val missingScopes: List<MissingScope>,
        // The per-provider system readiness checks, source of the app-client registration signal the
        // register-then-login flow reads (null when the status read failed — every provider then reads
        // "unknown" and routes through the credential card).
        val checks: SystemChecks? = null,
        val busy: BusyTarget?,
        val regrant: RegrantState?,
        // The in-flight secret-free bot device login (null unless one is awaiting approval at twitch.tv/activate).
        val botDevice: BotDeviceState? = null,
    ) : IntegrationsState

    data class Error(val detail: String) : IntegrationsState
}

/** The platform-shared bot account connection, surfaced to the screen. */
data class BotConnection(val connected: Boolean, val accountName: String?)

/** One provider row (Spotify / YouTube / Discord) the screen renders. */
data class ProviderConnection(
    val provider: String,
    val connected: Boolean,
    val accountName: String?,
    val needsReauth: Boolean,
)

/** The in-flight re-grant panel: the user code to enter and the Twitch URL to open. */
data class RegrantState(val userCode: String, val verificationUri: String)

/** The in-flight secret-free bot device login: the user code to enter and the Twitch URL to open. */
data class BotDeviceState(val userCode: String, val verificationUri: String)

/** Which row is mid-operation, so the screen can disable just that row's actions. */
sealed interface BusyTarget {
    data object Bot : BusyTarget

    data class Provider(val provider: String) : BusyTarget
}
