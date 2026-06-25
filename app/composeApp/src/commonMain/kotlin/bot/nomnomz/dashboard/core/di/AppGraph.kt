// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.di

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.LanDiscovery
import bot.nomnomz.dashboard.core.connection.OAuthConnectLauncher
import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.lanDiscovery
import bot.nomnomz.dashboard.core.feedback.FeedbackController
import bot.nomnomz.dashboard.core.i18n.LanguagePreferenceStore
import bot.nomnomz.dashboard.core.i18n.LanguageStore
import bot.nomnomz.dashboard.core.network.ApiClient
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatApi
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.DashboardApi
import bot.nomnomz.dashboard.core.network.DiscordApi
import bot.nomnomz.dashboard.core.network.EconomyApi
import bot.nomnomz.dashboard.core.network.EventStoreApi
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.MusicApi
import bot.nomnomz.dashboard.core.network.ParticipantApi
import bot.nomnomz.dashboard.core.network.PipelinesApi
import bot.nomnomz.dashboard.core.network.FeaturesApi
import bot.nomnomz.dashboard.core.network.FederationApi
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.core.network.QuotesApi
import bot.nomnomz.dashboard.core.network.RestAlertsApi
import bot.nomnomz.dashboard.core.network.RestAnalyticsApi
import bot.nomnomz.dashboard.core.io.JournalFileBridge
import bot.nomnomz.dashboard.core.network.RestChatApi
import bot.nomnomz.dashboard.core.network.RestCommandsApi
import bot.nomnomz.dashboard.core.network.RestCommunityApi
import bot.nomnomz.dashboard.core.network.RestDiscordApi
import bot.nomnomz.dashboard.core.network.RestEconomyApi
import bot.nomnomz.dashboard.core.network.RestEventStoreApi
import bot.nomnomz.dashboard.core.network.GamesApi
import bot.nomnomz.dashboard.core.network.RestGamesApi
import bot.nomnomz.dashboard.core.network.RestModerationApi
import bot.nomnomz.dashboard.core.network.RestMusicApi
import bot.nomnomz.dashboard.core.network.RestParticipantApi
import bot.nomnomz.dashboard.core.network.RestPipelinesApi
import bot.nomnomz.dashboard.core.network.RestFeaturesApi
import bot.nomnomz.dashboard.core.network.RestFederationApi
import bot.nomnomz.dashboard.core.network.RestWebhooksApi
import bot.nomnomz.dashboard.core.network.RestQuotesApi
import bot.nomnomz.dashboard.core.network.RestRewardsApi
import bot.nomnomz.dashboard.core.network.RestRolesApi
import bot.nomnomz.dashboard.core.network.RestSongRequestsApi
import bot.nomnomz.dashboard.core.network.RestStreamApi
import bot.nomnomz.dashboard.core.network.RestTimersApi
import bot.nomnomz.dashboard.core.network.RestTtsApi
import bot.nomnomz.dashboard.core.network.RestWidgetsApi
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.WidgetsApi
import bot.nomnomz.dashboard.core.network.RestAuthApi
import bot.nomnomz.dashboard.core.network.RestBotAuthApi
import bot.nomnomz.dashboard.core.network.RestChannelsApi
import bot.nomnomz.dashboard.core.network.RestDashboardApi
import bot.nomnomz.dashboard.core.network.RestIntegrationsApi
import bot.nomnomz.dashboard.core.network.RestSystemApi
import bot.nomnomz.dashboard.core.network.RestTwitchDiagnosticsApi
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import bot.nomnomz.dashboard.feature.alerts.state.AlertsController
import bot.nomnomz.dashboard.feature.analytics.state.AnalyticsController
import bot.nomnomz.dashboard.feature.chat.state.ChatController
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.discord.state.DiscordController
import bot.nomnomz.dashboard.feature.economy.state.EconomyController
import bot.nomnomz.dashboard.feature.home.state.HomeController
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.moderation.state.ModerationController
import bot.nomnomz.dashboard.feature.music.state.MusicController
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.features.state.FeaturesController
import bot.nomnomz.dashboard.feature.federation.state.FederationController
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksController
import bot.nomnomz.dashboard.feature.quotes.state.QuotesController
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.roles.state.RolesController
import bot.nomnomz.dashboard.feature.settings.state.JournalPortabilityController
import bot.nomnomz.dashboard.feature.settings.state.SettingsController
import bot.nomnomz.dashboard.feature.shell.state.ShellAccessController
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsController
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import bot.nomnomz.dashboard.feature.setup.state.SetupController

// The composition root for this slice — one instance of each engine singleton (frontend-structure.md
// F7: one HttpClient, one ConnectionStore), wired by explicit constructor injection. Koin replaces
// this hand graph 1:1 in the DI slice (frontend.md §11); the wiring shape is identical, so features
// don't change. App.kt holds one AppGraph for the app lifetime.
class AppGraph {
    val sessionStore: SessionStore = SessionStore()

    // The process-wide feedback bus: feature controllers emit success/error outcomes here and the single
    // shell-level FeedbackHost (App.kt) renders them on whatever page is mounted, so a result is visible
    // from one place and survives a page navigation / post-OAuth rebuild (replay).
    val feedbackController: FeedbackController = FeedbackController()

    // The display-language override — a per-install preference (LanguagePreferenceStore: file on desktop,
    // localStorage on web) driving the LanguageController, which App.kt feeds into the AppEnvironment to
    // force the UI language live, regardless of the OS/browser locale.
    private val languageStore: LanguageStore = LanguagePreferenceStore()

    val languageController: LanguageController = LanguageController(languageStore)

    // The single shared client reads base URL + token from the session on every request, so a
    // sign-in / connection switch re-targets the live client (frontend.md §3.1).
    val apiClient: ApiClient =
        ApiClient(
            baseUrlProvider = sessionStore::baseUrl,
            tokenProvider = sessionStore::accessToken,
        )

    val authApi: AuthApi = RestAuthApi(apiClient)
    val channelsApi: ChannelsApi = RestChannelsApi(apiClient)
    val botAuthApi: BotAuthApi = RestBotAuthApi(apiClient)
    val integrationsApi: IntegrationsApi = RestIntegrationsApi(apiClient)
    val twitchDiagnosticsApi: TwitchDiagnosticsApi = RestTwitchDiagnosticsApi(apiClient)
    val systemApi: SystemApi = RestSystemApi(apiClient)
    val dashboardApi: DashboardApi = RestDashboardApi(apiClient)
    val communityApi: CommunityApi = RestCommunityApi(apiClient)
    val commandsApi: CommandsApi = RestCommandsApi(apiClient)
    val timersApi: TimersApi = RestTimersApi(apiClient)
    val moderationApi: ModerationApi = RestModerationApi(apiClient)
    val analyticsApi: AnalyticsApi = RestAnalyticsApi(apiClient)
    val rewardsApi: RewardsApi = RestRewardsApi(apiClient)
    val songRequestsApi: SongRequestsApi = RestSongRequestsApi(apiClient)
    val ttsApi: TtsApi = RestTtsApi(apiClient)
    val gamesApi: GamesApi = RestGamesApi(apiClient)
    val streamApi: StreamApi = RestStreamApi(apiClient)
    val economyApi: EconomyApi = RestEconomyApi(apiClient)
    val alertsApi: AlertsApi = RestAlertsApi(apiClient)
    val widgetsApi: WidgetsApi = RestWidgetsApi(apiClient)
    val eventStoreApi: EventStoreApi = RestEventStoreApi(apiClient)
    val chatApi: ChatApi = RestChatApi(apiClient)
    val quotesApi: QuotesApi = RestQuotesApi(apiClient)
    val discordApi: DiscordApi = RestDiscordApi(apiClient)
    val rolesApi: RolesApi = RestRolesApi(apiClient)
    val musicApi: MusicApi = RestMusicApi(apiClient)
    val participantApi: ParticipantApi = RestParticipantApi(apiClient)
    val pipelinesApi: PipelinesApi = RestPipelinesApi(apiClient)
    val featuresApi: FeaturesApi = RestFeaturesApi(apiClient)
    val webhooksApi: WebhooksApi = RestWebhooksApi(apiClient)
    val federationApi: FederationApi = RestFederationApi(apiClient)

    private val oauthLauncher: OAuthLauncher = OAuthLauncher()
    private val connectLauncher: ConnectLauncher = OAuthConnectLauncher(oauthLauncher)

    // The per-target mDNS browse seam — jmDNS on desktop, a no-op on web (single-origin). Built via the
    // platform [lanDiscovery] factory like the other per-target engines (OAuthLauncher / TokenVault) and
    // handed to the Connect controller, which owns its start/stop lifecycle.
    private val lanDiscovery: LanDiscovery = lanDiscovery()

    val connectController: ConnectController =
        ConnectController(
            sessionStore = sessionStore,
            authApi = authApi,
            systemApi = systemApi,
            connectLauncher = connectLauncher,
            lanDiscovery = lanDiscovery,
        )

    // The first-run setup wizard's holder. On "continue to sign-in" it hands back to the connect
    // controller's streamer OAuth (signInStreamer), which establishes the session and advances the gate.
    val setupController: SetupController =
        SetupController(
            systemApi = systemApi,
            connectLauncher = connectLauncher,
            onReadyToSignIn = connectController::signInStreamer,
        )

    val integrationsController: IntegrationsController =
        IntegrationsController(
            sessionStore = sessionStore,
            channelsApi = channelsApi,
            botAuthApi = botAuthApi,
            integrationsApi = integrationsApi,
            connectLauncher = connectLauncher,
            diagnosticsApi = twitchDiagnosticsApi,
            authApi = authApi,
            systemApi = systemApi,
            feedback = feedbackController,
        )

    val homeController: HomeController =
        HomeController(channelsApi = channelsApi, dashboardApi = dashboardApi)

    val communityController: CommunityController =
        CommunityController(channelsApi = channelsApi, communityApi = communityApi)

    val commandsController: CommandsController =
        CommandsController(channelsApi = channelsApi, commandsApi = commandsApi, feedback = feedbackController)

    val timersController: TimersController =
        TimersController(channelsApi = channelsApi, timersApi = timersApi)

    val moderationController: ModerationController =
        ModerationController(channelsApi = channelsApi, moderationApi = moderationApi, feedback = feedbackController)

    val analyticsController: AnalyticsController =
        AnalyticsController(channelsApi = channelsApi, analyticsApi = analyticsApi)

    val rewardsController: RewardsController =
        RewardsController(channelsApi = channelsApi, rewardsApi = rewardsApi)

    val songRequestsController: SongRequestsController =
        SongRequestsController(channelsApi = channelsApi, songRequestsApi = songRequestsApi)

    val ttsController: TtsController = TtsController(channelsApi = channelsApi, ttsApi = ttsApi)

    val gamesController: GamesController =
        GamesController(channelsApi = channelsApi, gamesApi = gamesApi)

    val settingsController: SettingsController =
        SettingsController(channelsApi = channelsApi, streamApi = streamApi)

    val twitchAppCredentialsController: TwitchAppCredentialsController =
        TwitchAppCredentialsController(
            systemApi = systemApi,
            baseUrlProvider = sessionStore::baseUrl,
            feedback = feedbackController,
        )

    // The per-target OS file save/pick seam for journal export/import, built like the other platform engines.
    private val journalFileBridge: JournalFileBridge = JournalFileBridge()

    val journalPortabilityController: JournalPortabilityController =
        JournalPortabilityController(
            channelsApi = channelsApi,
            eventStoreApi = eventStoreApi,
            fileBridge = journalFileBridge,
        )

    val economyController: EconomyController =
        EconomyController(channelsApi = channelsApi, economyApi = economyApi)

    val alertsController: AlertsController =
        AlertsController(channelsApi = channelsApi, alertsApi = alertsApi)

    val widgetsController: WidgetsController =
        WidgetsController(channelsApi = channelsApi, widgetsApi = widgetsApi)

    val chatController: ChatController =
        ChatController(channelsApi = channelsApi, chatApi = chatApi)

    val quotesController: QuotesController =
        QuotesController(quotesApi = quotesApi, feedback = feedbackController)

    val discordController: DiscordController =
        DiscordController(channelsApi = channelsApi, discordApi = discordApi)

    val rolesController: RolesController =
        RolesController(channelsApi = channelsApi, rolesApi = rolesApi)

    // The shell's role resolver — fetches the caller's own /effective/me on session establish so the sidebar and
    // every write affordance gate by the REAL Plane-B ManagementRole, replacing the old broadcaster hardcode.
    val shellAccessController: ShellAccessController =
        ShellAccessController(channelsApi = channelsApi, rolesApi = rolesApi)

    val musicController: MusicController =
        MusicController(channelsApi = channelsApi, musicApi = musicApi)

    val pipelinesController: PipelinesController =
        PipelinesController(channelsApi = channelsApi, pipelinesApi = pipelinesApi, feedback = feedbackController)

    val featuresController: FeaturesController =
        FeaturesController(channelsApi = channelsApi, featuresApi = featuresApi)

    val webhooksController: WebhooksController =
        WebhooksController(channelsApi = channelsApi, webhooksApi = webhooksApi)

    val federationController: FederationController =
        FederationController(channelsApi = channelsApi, federationApi = federationApi)

    // The PARTICIPANT rung's controller is built PER resolved access (channel + caller's own user GUID + community
    // standing + permit capabilities), which the shell resolves at entry via /effective/me — unlike the management
    // controllers it needs that context up front, so it is a factory rather than a singleton. The shell calls this
    // once the access resolves and remembers the result for the participant surface's lifetime.
    fun participantController(
        channelId: String,
        userId: String?,
        standing: ParticipantStanding,
        capabilities: List<String>,
    ): ParticipantController =
        ParticipantController(
            channelId = channelId,
            userId = userId,
            standing = standing,
            capabilities = capabilities,
            participantApi = participantApi,
            dashboardApi = dashboardApi,
            economyApi = economyApi,
            musicApi = musicApi,
        )
}
