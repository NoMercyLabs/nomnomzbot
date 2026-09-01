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
import bot.nomnomz.dashboard.core.network.EventSubReconcileReport
import bot.nomnomz.dashboard.core.network.EventSubSubscription
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_connect_failed
import nomnomzbot.composeapp.generated.resources.feedback_regrant_failed
import nomnomzbot.composeapp.generated.resources.feedback_credentials_saved
import nomnomzbot.composeapp.generated.resources.feedback_disconnect_failed
import nomnomzbot.composeapp.generated.resources.feedback_disconnected
import org.jetbrains.compose.resources.StringResource
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.ApiError

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
    // Read for the per-provider app-client registration signal (checks.spotify/discord) AND to choose the
    // streamer SCOPE-REGRANT method off the secret-present signal (twitchApp.ok): a configured client secret
    // unlocks the one-tap redirect re-grant (the streamer is the logged-in account); without one, the
    // secret-free device-code re-grant is used. A Twitch client secret is optional throughout.
    private val systemApi: SystemApi,
    private val feedback: Feedback = NoOpFeedback,
    // Whether this build runs in the browser (web) vs the native desktop app. Only the WEB re-grant can use
    // the seamless redirect: it rides the dashboard session cookie so the backend widens the scope set to
    // base ∪ granted ∪ missing. The desktop re-grant opens the SYSTEM browser, which carries no dashboard
    // cookie, so a redirect there would request only the static base set and never clear a runtime-detected
    // gap — desktop must always take the device path (whose scope set the backend computes server-side).
    // Defaults to web; AppGraph passes the real per-platform value (`servedOriginProfile() != null`).
    private val isWeb: Boolean = true,
) {
    private val _state: MutableStateFlow<IntegrationsState> =
        MutableStateFlow(IntegrationsState.Loading)

    /** The screen's render state: loading / ready (with per-provider rows) / error. */
    val state: StateFlow<IntegrationsState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load the bot + integration statuses. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is IntegrationsState.Ready) _state.value = IntegrationsState.Loading

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

        // A genuine fetch FAILURE (network/5xx/etc.) must never silently render as "zero integrations
        // connected" — that would hide a broken backend behind an innocent-looking empty state (Dashboard
        // reflects backend API / Truthful data hard rules). Distinguish the two: Error is a distinct state
        // the screen renders as a retry-able banner, never folded into Ready with an empty provider list.
        val providers: List<ProviderConnection> =
            when (val result: ApiResult<List<IntegrationStatus>> = integrationsApi.status(id)) {
                is ApiResult.Ok -> result.value.map { it.toProviderConnection() }
                is ApiResult.Failure -> {
                    _state.value = IntegrationsState.Error(result.error.message)
                    return
                }
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

        val eventSubSubscriptions: List<EventSubSubscription> =
            when (val result: ApiResult<List<EventSubSubscription>> = diagnosticsApi.subscriptions(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        // Spotify's own registration signal: channel-scoped, never the system-level `checks.spotify` (S-OWN10
        // — the bot never hosts a shared Spotify app, so there is no system-level Spotify registration to read
        // any more). A read failure leaves it null, which the connect flow treats like "not registered".
        val spotifyOwnCredentialsConfigured: Boolean? =
            when (val result: ApiResult<bot.nomnomz.dashboard.core.network.ChannelSpotifyCredentials> =
                integrationsApi.spotifyCredentials(id)) {
                is ApiResult.Ok -> result.value.clientId != null && result.value.hasClientSecret
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
                spotifyOwnCredentialsConfigured = spotifyOwnCredentialsConfigured,
                busy = null,
                regrant = ready?.regrant,
                botDevice = ready?.botDevice,
                eventSubSubscriptions = eventSubSubscriptions,
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
        val ready: IntegrationsState.Ready = (_state.value as? IntegrationsState.Ready) ?: return null
        if (provider.equals("spotify", ignoreCase = true)) {
            // Spotify's registration is channel-scoped, not a system check (S-OWN10) — see
            // [IntegrationsState.Ready.spotifyOwnCredentialsConfigured].
            return ready.spotifyOwnCredentialsConfigured
        }
        val checks: SystemChecks = ready.checks ?: return null
        val check: SystemCheck? =
            when (provider.lowercase()) {
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
     * Register the operator's own BYOC app credentials for [provider] (Spotify/YouTube/Discord) then re-read
     * status so [clientRegistered] reflects the backend. The client id is required (a blank id is a client-
     * side guard); the secret rides as typed. On success the feedback host announces it and the caller
     * proceeds to OAuth; a failure surfaces on the host and leaves the screen on the credential step. Returns
     * whether the save succeeded.
     *
     * Spotify is CHANNEL-scoped, not system-scoped (S-OWN10: the bot never hosts a shared/system-level
     * Spotify app — every channel must register and use its own app), so it routes through
     * [IntegrationsApi.saveSpotifyCredentials] rather than the wizard's system-level credential endpoint every
     * other provider here uses.
     */
    suspend fun saveProviderCredentials(
        provider: String,
        clientId: String,
        clientSecret: String,
    ): Boolean {
        val id: String = clientId.trim()
        if (id.isEmpty()) return false

        if (provider.equals("spotify", ignoreCase = true)) {
            val channel: String = channelId ?: return false
            val spotifyResult: ApiResult<bot.nomnomz.dashboard.core.network.ChannelSpotifyCredentials> =
                integrationsApi.saveSpotifyCredentials(channel, id, clientSecret.trim())
            return when (spotifyResult) {
                is ApiResult.Failure -> {
                    feedback.error(Res.string.feedback_connect_failed, spotifyResult.error.message)
                    false
                }
                is ApiResult.Ok -> {
                    feedback.success(Res.string.feedback_credentials_saved)
                    refresh()
                    true
                }
            }
        }

        // kick_bot has no credential slot of its own — it shares Kick's ("kick") app client
        // (OAuthProviderRegistry: CredentialsProvider="kick" for both), so its save routes there too.
        val credentialsProvider: String =
            if (provider.equals("kick_bot", ignoreCase = true)) "kick" else provider.lowercase()

        val result: ApiResult<Unit> =
            when (credentialsProvider) {
                "youtube", "discord", "kick" ->
                    systemApi.saveCredentials(credentialsProvider, id, clientSecret.trim())
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
     * Connect the platform-shared bot account. The bot is a SEPARATE Twitch account, so this ALWAYS uses the
     * device-code flow — never the redirect. A redirect would authorize whatever account is logged into the
     * browser (the streamer, whose name then wrongly shows on the consent), not the bot; the device-code flow
     * is account-agnostic, so the operator approves the short code at twitch.tv/activate on a session logged
     * in as the bot. (The streamer's OWN re-grant is the opposite — a seamless redirect; see [regrantScopes].)
     */
    suspend fun connectBot() = connectBotViaDevice()

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

    /**
     * Run a generic connect (Spotify/YouTube/Kick/…) for [provider] with [scopeSetKey], then refresh.
     *
     * A provider with no app client configured (no BYOC, no shared credentials) answers the start call with
     * `PROVIDER_NOT_CONFIGURED` (`ChannelCredentialsResolver`/`IntegrationOAuthService.StartConnectAsync`) —
     * this is a client-actionable "register your own app first" state, never a generic error, so it routes to
     * [IntegrationsState.Ready.onboardingProvider] (the screen opens the BYOC credential dialog) instead of an
     * error toast. This is the reactive backstop for providers with no pre-connect registration check in the
     * UI (Kick/Kick bot; Spotify/YouTube/Discord already pre-check via [clientRegistered] and never hit it).
     * Any OTHER failure still surfaces as the normal error toast.
     */
    suspend fun connectProvider(provider: String, scopeSetKey: String) {
        val id: String = channelId ?: return
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(busy = BusyTarget.Provider(provider))

        val result: ApiResult<OAuthStart> =
            connectLauncherAwaitOAuthStart(id, provider, scopeSetKey)

        when (result) {
            is ApiResult.Ok -> Unit // the launcher already opened the authorize URL; nothing else to announce.
            is ApiResult.Failure ->
                if (result.error.code == PROVIDER_NOT_CONFIGURED_CODE) {
                    refresh()
                    val reOpened: IntegrationsState.Ready =
                        _state.value as? IntegrationsState.Ready ?: return
                    _state.value = reOpened.copy(onboardingProvider = provider)
                    return
                } else {
                    feedback.error(Res.string.feedback_connect_failed, result.error.message)
                }
        }
        refresh()
    }

    /** Dismiss the reactive BYOC-onboarding dialog opened by [connectProvider] without connecting. */
    fun dismissOnboarding() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        _state.value = ready.copy(onboardingProvider = null)
    }

    /**
     * Save [provider]'s BYOC credentials from the reactive onboarding dialog, then retry the connect that
     * triggered it. Delegates the save to [saveProviderCredentials] (the same endpoint the branded modal uses)
     * so there is exactly one save path; on success the dialog closes and [connectProvider] runs again — this
     * time the client is registered, so it proceeds straight to OAuth.
     */
    suspend fun saveOnboardingCredentialsAndRetry(
        provider: String,
        scopeSetKey: String,
        clientId: String,
        clientSecret: String,
    ) {
        dismissOnboarding()
        if (saveProviderCredentials(provider, clientId, clientSecret)) connectProvider(provider, scopeSetKey)
    }

    /** Start the generic OAuth connect and open the authorize URL, returning the raw start result. */
    private suspend fun connectLauncherAwaitOAuthStart(
        channelId: String,
        provider: String,
        scopeSetKey: String,
    ): ApiResult<OAuthStart> {
        var startResult: ApiResult<OAuthStart> = ApiResult.Failure(
            ApiError(status = 0, code = "NOT_STARTED", message = "Connect did not start.")
        )
        connectLauncher.awaitConnect { redirect ->
            val started: ApiResult<OAuthStart> =
                integrationsApi.startGenericConnect(
                    channelId = channelId,
                    provider = provider,
                    scopeSetKey = scopeSetKey,
                    returnUrl = redirect.ifBlank { null },
                )
            startResult = started
            started.mapToAuthorizeUrl()
        }
        return startResult
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
    /**
     * The real, backend-counted blast radius of disconnecting [provider] (S-CONSEQ) — what STOPS WORKING, not
     * what is deleted. Rendered in the confirm BEFORE the disconnect; a provider that is not connected, or an
     * unresolved channel, is a genuine failure the dialog reports on its own rather than a reassuring zero.
     */
    suspend fun fetchDisconnectBlastRadius(provider: String): ApiResult<BlastRadiusSummary> {
        val id: String =
            channelId
                ?: return ApiResult.Failure(
                    ApiError(status = 0, code = "NO_CHANNEL", message = "No active channel.")
                )
        return integrationsApi.disconnectBlastRadius(id, provider)
    }

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
     * Start the one-click additive scope re-grant for the streamer's OWN account. Dispatches by whether a
     * client secret is configured: the seamless REDIRECT re-grant when it is (the streamer is the logged-in
     * account — one tap, no code), else the secret-free DEVICE-CODE re-grant. Single-flight against an
     * in-flight device panel. Either way the widened grant reconciles server-side and the gaps re-read clear.
     */
    suspend fun regrantScopes() {
        val ready: IntegrationsState.Ready = _state.value as? IntegrationsState.Ready ?: return
        if (ready.regrant != null) return // single-flight: a device re-grant is already in progress.

        // The `regrant` panel above only exists once a start SUCCEEDED, so it cannot guard the start
        // itself: every click used to fire another startRegrant(), which is how spamming the button
        // earned a rate-limit instead of an answer. This flag covers the in-flight start too.
        if (regrantStarting) return
        regrantStarting = true
        try {
            regrantScopesInner(ready)
        } finally {
            regrantStarting = false
        }
    }

    /** Guards the START call itself — see [regrantScopes]. */
    private var regrantStarting: Boolean = false

    private suspend fun regrantScopesInner(ready: IntegrationsState.Ready) {

        // The redirect re-grant's authorize URL must stay short (a ~79-scope, 2301-char URL 502'd on Twitch's
        // own end once — identity-auth's progressive-scopes fix), so the backend only ever widens it by scopes
        // ACTUALLY recorded missing (a live 403 already hit) — never the proactively-detected rest of the
        // catalogue this banner also surfaces (AuthService.WidenedStreamerScopesAsync). A scope the code needs
        // but has never been exercised live (detectedAtRuntime == false) is therefore invisible to the redirect
        // no matter how many times it's clicked — the device-code flow has no such URL, so it's the only path
        // that can actually close those gaps. Falling back to it here (instead of always preferring the
        // seamless redirect) is what makes the button work for every gap the banner reports, not just the
        // reactively-detected subset.
        val hasProactiveOnlyGap: Boolean = ready.missingScopes.any { !it.detectedAtRuntime }

        // On WEB, the streamer IS the logged-in account, so prefer the seamless REDIRECT re-grant when a client
        // secret is configured (twitchApp.ok) AND every gap is one it can actually close: one tap → Twitch →
        // back, re-vaulting the full scope set with no code to type. The secret-less public client (which can't
        // exchange a code), ALWAYS the desktop app (its system browser carries no dashboard cookie, so the
        // redirect can't widen scopes there), and any proactive-only gap all fall back to the device-code path,
        // whose additive scope set the backend computes server-side with no URL-length ceiling.
        if (isWeb && ready.checks?.twitchApp?.ok == true && !hasProactiveOnlyGap)
            regrantScopesViaRedirect()
        else regrantScopesViaDevice(ready)
    }

    /**
     * The seamless re-grant for a secret-configured client: run the streamer authorize REDIRECT — the same
     * flow login/reconnect use — which re-vaults the streamer token with the full scope set, covering every
     * gap, then re-read the now-cleared gaps. On web the page navigates to Twitch and the widened grant
     * re-establishes on return; on desktop the loopback resolves and we refresh here. A declined attempt just
     * leaves the gaps intact (the re-read reflects that).
     */
    private suspend fun regrantScopesViaRedirect() {
        val base: String = sessionStore.baseUrl() ?: return
        connectLauncher.authorizeStreamer(base)
        refresh()
    }

    /**
     * The secret-free re-grant: the backend mints a Device Code Flow handle requesting (granted ∪ missing); we
     * surface the user code + verification URL (the screen opens it) and poll the normal streamer device poll
     * until the operator approves at twitch.tv/activate — on approval the widened grant reconciles server-side,
     * so we re-read the gaps (they clear) and dismiss the panel. A failed START (e.g. nothing missing) leaves
     * the screen unchanged; the poll loop tolerates the code expiring.
     */
    private suspend fun regrantScopesViaDevice(ready: IntegrationsState.Ready) {
        when (val start: ApiResult<ScopeRegrantStart> = diagnosticsApi.startRegrant()) {
            // NEVER a silent return. A button that reports nothing is indistinguishable from a broken
            // one, so the streamer clicks again and again — which is exactly how this earned a rate-limit
            // instead of an explanation. Surface the real backend reason.
            is ApiResult.Failure ->
                feedback.error(Res.string.feedback_regrant_failed, start.error.message)
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

            when (
                val poll: ApiResult<DeviceLoginPoll> =
                    authApi.pollDeviceLogin(deviceCode = start.deviceCode)
            ) {
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
            loginOnly = loginOnly,
        )

    /**
     * Reconcile this channel's EventSub registry against Twitch's actual subscription list.
     * Re-creates missing subscriptions and prunes orphans; reloads the list on completion.
     */
    suspend fun reconcileEventSub(): EventSubReconcileReport? {
        val id: String = channelId ?: return null
        return when (val result: ApiResult<EventSubReconcileReport> = diagnosticsApi.reconcile(id)) {
            is ApiResult.Ok -> {
                refresh()
                result.value
            }
            is ApiResult.Failure -> null
        }
    }

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

        // ChannelCredentialsResolver / IntegrationOAuthService.StartConnectAsync's Result.ErrorCode when a
        // provider has no app client (BYOC or shared) configured — see connectProvider's doc.
        const val PROVIDER_NOT_CONFIGURED_CODE = "PROVIDER_NOT_CONFIGURED"
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
        // Whether THIS channel has registered its own Spotify BYOC app client (S-OWN10: the bot never hosts
        // a shared/system-level Spotify app, so unlike [checks], Spotify's registration signal is channel-
        // scoped, not system-scoped). Null when the read failed — the connect flow then routes through the
        // credential card same as `false`.
        val spotifyOwnCredentialsConfigured: Boolean? = null,
        val busy: BusyTarget?,
        val regrant: RegrantState?,
        // The in-flight secret-free bot device login (null unless one is awaiting approval at twitch.tv/activate).
        val botDevice: BotDeviceState? = null,
        // Registered EventSub topics; empty when the read failed (non-fatal).
        val eventSubSubscriptions: List<EventSubSubscription> = emptyList(),
        // The provider whose connect just answered PROVIDER_NOT_CONFIGURED (null unless one just did): the
        // screen opens the generic BYOC onboarding dialog for it instead of an error toast (S-OWN07). Set by
        // IntegrationsController.connectProvider, cleared by dismissOnboarding / a successful save+retry.
        val onboardingProvider: String? = null,
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
    // True when the channel owner signed in with this provider but never granted the actual platform
    // connection (currently only meaningful for Kick — see IntegrationStatus.loginOnly).
    val loginOnly: Boolean = false,
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
