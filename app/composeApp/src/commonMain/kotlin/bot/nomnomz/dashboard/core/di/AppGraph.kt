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
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.AlertsApi
import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.RestBuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelProvisioningApi
import bot.nomnomz.dashboard.core.network.EngagementApi
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
import bot.nomnomz.dashboard.core.network.CodeScriptsApi
import bot.nomnomz.dashboard.core.network.WebhooksApi
import bot.nomnomz.dashboard.core.network.CustomEventsApi
import bot.nomnomz.dashboard.core.network.RestCustomEventsApi
import bot.nomnomz.dashboard.core.network.PickListsApi
import bot.nomnomz.dashboard.core.network.QuotesApi
import bot.nomnomz.dashboard.core.network.RestSoundApi
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.RestAlertsApi
import bot.nomnomz.dashboard.core.network.RestAnalyticsApi
import bot.nomnomz.dashboard.core.io.AudioFilePicker
import bot.nomnomz.dashboard.core.io.JournalFileBridge
import bot.nomnomz.dashboard.core.network.RestChatApi
import bot.nomnomz.dashboard.core.network.RestCommandsApi
import bot.nomnomz.dashboard.core.network.RestCommunityApi
import bot.nomnomz.dashboard.core.network.RestDiscordApi
import bot.nomnomz.dashboard.core.network.RestEconomyApi
import bot.nomnomz.dashboard.core.network.RestEventStoreApi
import bot.nomnomz.dashboard.core.network.EventResponsesApi
import bot.nomnomz.dashboard.core.network.RestEventResponsesApi
import bot.nomnomz.dashboard.core.network.GamesApi
import bot.nomnomz.dashboard.core.network.RestGamesApi
import bot.nomnomz.dashboard.core.network.GiveawaysApi
import bot.nomnomz.dashboard.core.network.RestGiveawaysApi
import bot.nomnomz.dashboard.core.network.SupportersApi
import bot.nomnomz.dashboard.core.network.RestSupportersApi
import bot.nomnomz.dashboard.core.network.RestModerationApi
import bot.nomnomz.dashboard.core.network.RestMusicApi
import bot.nomnomz.dashboard.core.network.RestParticipantApi
import bot.nomnomz.dashboard.core.network.RestPipelinesApi
import bot.nomnomz.dashboard.core.network.RestFeaturesApi
import bot.nomnomz.dashboard.core.network.RestFederationApi
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminApiImpl
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.PlatformAdminApiImpl
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.PlatformIamApiImpl
import bot.nomnomz.dashboard.core.network.PronounsApi
import bot.nomnomz.dashboard.core.network.PronounsApiImpl
import bot.nomnomz.dashboard.core.network.BillingApi
import bot.nomnomz.dashboard.core.network.RestBillingApi
import bot.nomnomz.dashboard.core.network.RestCodeScriptsApi
import bot.nomnomz.dashboard.core.network.RestWebhooksApi
import bot.nomnomz.dashboard.core.network.RestPickListsApi
import bot.nomnomz.dashboard.core.network.RestQuotesApi
import bot.nomnomz.dashboard.core.network.RestRewardsApi
import bot.nomnomz.dashboard.core.network.RestRolesApi
import bot.nomnomz.dashboard.core.network.RestSongRequestsApi
import bot.nomnomz.dashboard.core.network.RestStreamApi
import bot.nomnomz.dashboard.core.network.RestTimersApi
import bot.nomnomz.dashboard.core.network.RestTtsApi
import bot.nomnomz.dashboard.core.editor.ProjectEditor
import bot.nomnomz.dashboard.core.editor.ProjectEditorIO
import bot.nomnomz.dashboard.core.network.RestSdkTypesApi
import bot.nomnomz.dashboard.core.network.SdkTypesApi
import bot.nomnomz.dashboard.core.network.RestWidgetGalleryApi
import bot.nomnomz.dashboard.core.network.RestWidgetsApi
import bot.nomnomz.dashboard.core.network.RewardsApi
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.TimersApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.WidgetGalleryApi
import bot.nomnomz.dashboard.core.network.WidgetsApi
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.RestUsersApi
import bot.nomnomz.dashboard.core.network.RestViewerDataApi
import bot.nomnomz.dashboard.core.network.ViewerDataApi
import bot.nomnomz.dashboard.core.network.RestAuthApi
import bot.nomnomz.dashboard.core.network.RestBotAuthApi
import bot.nomnomz.dashboard.core.network.RestChannelSettingsApi
import bot.nomnomz.dashboard.core.network.RestChannelProvisioningApi
import bot.nomnomz.dashboard.core.network.RestEngagementApi
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
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesController
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.giveaways.state.GiveawaysController
import bot.nomnomz.dashboard.feature.supporters.state.SupportersController
import bot.nomnomz.dashboard.feature.moderation.state.ModerationController
import bot.nomnomz.dashboard.feature.music.state.MusicController
import bot.nomnomz.dashboard.feature.participant.state.ParticipantController
import bot.nomnomz.dashboard.feature.shell.nav.ParticipantStanding
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.features.state.FeaturesController
import bot.nomnomz.dashboard.feature.federation.state.FederationController
import bot.nomnomz.dashboard.feature.codescripts.state.CodeScriptsController
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksController
import bot.nomnomz.dashboard.feature.customevents.state.CustomEventsController
import bot.nomnomz.dashboard.feature.picklists.state.PickListsController
import bot.nomnomz.dashboard.feature.quotes.state.QuotesController
import bot.nomnomz.dashboard.feature.sound.state.SoundController
import bot.nomnomz.dashboard.feature.rewards.state.RewardsController
import bot.nomnomz.dashboard.feature.roles.state.RolesController
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.settings.state.BillingController
import bot.nomnomz.dashboard.feature.settings.state.ChannelBotController
import bot.nomnomz.dashboard.feature.settings.state.JournalPortabilityController
import bot.nomnomz.dashboard.feature.settings.state.EngagementController
import bot.nomnomz.dashboard.feature.settings.state.PersonalityController
import bot.nomnomz.dashboard.feature.settings.state.SettingsController
import bot.nomnomz.dashboard.feature.shell.state.ChannelSwitcherController
import bot.nomnomz.dashboard.feature.shell.state.ShellAccessController
import bot.nomnomz.dashboard.feature.settings.state.TwitchAppCredentialsController
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsController
import bot.nomnomz.dashboard.core.network.LiveOpsApi
import bot.nomnomz.dashboard.core.network.RestLiveOpsApi
import bot.nomnomz.dashboard.core.realtime.AdminHubClient
import bot.nomnomz.dashboard.core.realtime.DashboardHubClient
import bot.nomnomz.dashboard.feature.language.state.LanguageController
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import bot.nomnomz.dashboard.feature.liveops.state.LiveOpsController
import bot.nomnomz.dashboard.feature.liveops.state.ScheduleController
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

    // The SignalR hub client for real-time server push (DashboardHub). Shared across controllers
    // that need live updates (ChatController → subscribeToHub). Connected once when a session is
    // established; the connect call is idempotent (re-entrant no-op when already connected).
    val dashboardHubClient: DashboardHubClient = DashboardHubClient()

    // The SignalR hub client for the platform-operator hub (AdminHub, /hubs/admin). The handshake is gated on
    // the caller's iam:manage grant, so it only ever establishes for a privileged admin; the AdminController
    // subscribes to it for the live status panel + channel registry (no polling).
    val adminHubClient: AdminHubClient = AdminHubClient()

    // The streamer's Twitch chat color (#RRGGBB) — null until HomeController resolves the primary channel.
    // App.kt reads this to supply the dynamic accent to NomNomzTheme (design-system §2).
    private val _chatAccentColor: MutableStateFlow<String?> = MutableStateFlow(null)
    val chatAccentColor: StateFlow<String?> = _chatAccentColor.asStateFlow()

    internal fun setChatAccentColor(hex: String?) {
        _chatAccentColor.value = hex
    }

    // The single shared client reads base URL + token from the session on every request, so a
    // sign-in / connection switch re-targets the live client (frontend.md §3.1).
    val apiClient: ApiClient =
        ApiClient(
            baseUrlProvider = sessionStore::baseUrl,
            tokenProvider = sessionStore::accessToken,
            // Every request carries the operator's active channel as X-Channel-Id, so a switch in the channel
            // switcher retargets the whole dashboard (mod tools included) — not just the endpoints whose route
            // already threads {channelId}. Null before a channel is chosen ⇒ the caller's own channel.
            channelProvider = { sessionStore.activeChannelId.value },
        )

    val authApi: AuthApi = RestAuthApi(apiClient)

    // Shared JWT refresher: POST /auth/refresh (against the HttpOnly cookie on web, the stored refresh token
    // on native) and store the new access token; returns true when a fresh token was obtained. Used by BOTH
    // the REST 401→refresh→retry interceptor AND the SignalR hub client — a raw WebSocket has no HTTP
    // interceptor, so without a refresher of its own an expired token would 401-storm the handshake forever on
    // an idle session (no REST call fires to refresh it). One definition, two consumers.
    val tokenRefresher: suspend () -> Boolean = {
        when (val result: ApiResult<AuthPayload> = authApi.refresh(null)) {
            is ApiResult.Ok -> {
                sessionStore.updateAccessToken(result.value.accessToken)
                true
            }
            is ApiResult.Failure -> false
        }
    }

    init {
        // Wire the 401→refresh→retry interceptor. ApiClient and AuthApi are both ready at this point.
        // On any 401 (except the refresh endpoint itself), ApiClient silently calls refresh(), stores
        // the new JWT, and retries the original request — so stale-token failures are invisible to callers.
        apiClient.tokenRefresher = tokenRefresher
    }

    val channelsApi: ChannelsApi = RestChannelsApi(apiClient, sessionStore)
    val botAuthApi: BotAuthApi = RestBotAuthApi(apiClient)
    val integrationsApi: IntegrationsApi = RestIntegrationsApi(apiClient)
    val twitchDiagnosticsApi: TwitchDiagnosticsApi = RestTwitchDiagnosticsApi(apiClient)
    val systemApi: SystemApi = RestSystemApi(apiClient)
    val dashboardApi: DashboardApi = RestDashboardApi(apiClient)
    val communityApi: CommunityApi = RestCommunityApi(apiClient)
    val usersApi: UsersApi = RestUsersApi(apiClient)
    val viewerDataApi: ViewerDataApi = RestViewerDataApi(apiClient)
    val commandsApi: CommandsApi = RestCommandsApi(apiClient)
    val builtinsApi: BuiltinsApi = RestBuiltinsApi(apiClient)
    val timersApi: TimersApi = RestTimersApi(apiClient)
    val moderationApi: ModerationApi = RestModerationApi(apiClient)
    val analyticsApi: AnalyticsApi = RestAnalyticsApi(apiClient)
    val rewardsApi: RewardsApi = RestRewardsApi(apiClient)
    val songRequestsApi: SongRequestsApi = RestSongRequestsApi(apiClient)
    val ttsApi: TtsApi = RestTtsApi(apiClient)
    val gamesApi: GamesApi = RestGamesApi(apiClient)
    val eventResponsesApi: EventResponsesApi = RestEventResponsesApi(apiClient)
    val streamApi: StreamApi = RestStreamApi(apiClient)
    val channelSettingsApi: ChannelSettingsApi = RestChannelSettingsApi(apiClient)
    val engagementApi: EngagementApi = RestEngagementApi(apiClient)
    val economyApi: EconomyApi = RestEconomyApi(apiClient)
    val alertsApi: AlertsApi = RestAlertsApi(apiClient)
    val widgetsApi: WidgetsApi = RestWidgetsApi(apiClient)
    val widgetGalleryApi: WidgetGalleryApi = RestWidgetGalleryApi(apiClient)
    val eventStoreApi: EventStoreApi = RestEventStoreApi(apiClient)
    val chatApi: ChatApi = RestChatApi(apiClient)
    val quotesApi: QuotesApi = RestQuotesApi(apiClient)
    val pickListsApi: PickListsApi = RestPickListsApi(apiClient)
    val giveawaysApi: GiveawaysApi = RestGiveawaysApi(apiClient)
    val supportersApi: SupportersApi = RestSupportersApi(apiClient)
    val soundApi: SoundApi = RestSoundApi(apiClient)
    val discordApi: DiscordApi = RestDiscordApi(apiClient)
    val rolesApi: RolesApi = RestRolesApi(apiClient)
    val musicApi: MusicApi = RestMusicApi(apiClient)
    val participantApi: ParticipantApi = RestParticipantApi(apiClient)
    val pipelinesApi: PipelinesApi = RestPipelinesApi(apiClient)
    val featuresApi: FeaturesApi = RestFeaturesApi(apiClient)
    val webhooksApi: WebhooksApi = RestWebhooksApi(apiClient)
    val customEventsApi: CustomEventsApi = RestCustomEventsApi(apiClient)
    val federationApi: FederationApi = RestFederationApi(apiClient)
    val codeScriptsApi: CodeScriptsApi = RestCodeScriptsApi(apiClient)
    // The generated dev-platform SDK type declarations (nnz.d.ts) — fetched by the code editor so a later slice
    // can wire TypeScript autocomplete/inline errors over the SDK surface (fetch is ready now).
    val sdkTypesApi: SdkTypesApi = RestSdkTypesApi(apiClient)
    // One shared multi-file project editor actual, injected into every screen that edits a dev-platform project.
    val projectEditor: ProjectEditorIO = ProjectEditor()
    val liveOpsApi: LiveOpsApi = RestLiveOpsApi(apiClient)
    val billingApi: BillingApi = RestBillingApi(apiClient)
    val adminApi: AdminApi = AdminApiImpl(apiClient)
    val platformIamApi: PlatformIamApi = PlatformIamApiImpl(apiClient)
    val platformAdminApi: PlatformAdminApi = PlatformAdminApiImpl(apiClient)
    val pronounsApi: PronounsApi = PronounsApiImpl(apiClient)

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
            diagnosticsApi = twitchDiagnosticsApi,
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

    val channelProvisioningApi: ChannelProvisioningApi = RestChannelProvisioningApi(apiClient)

    val channelSwitcherController: ChannelSwitcherController =
        ChannelSwitcherController(
            channelsApi = channelsApi,
            provisioningApi = channelProvisioningApi,
            sessionStore = sessionStore,
            // Re-theme the shell on channel switch (same accent hook HomeController uses on load).
            onChatColorResolved = ::setChatAccentColor,
        )

    val homeController: HomeController =
        HomeController(
            channelsApi = channelsApi,
            dashboardApi = dashboardApi,
            streamApi = streamApi,
            commandsApi = commandsApi,
            hubClient = dashboardHubClient,
            baseUrl = sessionStore::baseUrl,
            accessToken = sessionStore::accessToken,
            onChatColorResolved = ::setChatAccentColor,
        )

    val communityController: CommunityController =
        CommunityController(
            channelsApi = channelsApi,
            communityApi = communityApi,
            usersApi = usersApi,
            viewerDataApi = viewerDataApi,
        )

    val commandsController: CommandsController =
        CommandsController(
            channelsApi = channelsApi,
            commandsApi = commandsApi,
            builtinsApi = builtinsApi,
            pipelinesApi = pipelinesApi,
            feedback = feedbackController,
        )

    val timersController: TimersController =
        TimersController(
            channelsApi = channelsApi,
            timersApi = timersApi,
            pipelinesApi = pipelinesApi,
        )

    val moderationController: ModerationController =
        ModerationController(channelsApi = channelsApi, moderationApi = moderationApi, feedback = feedbackController)

    val analyticsController: AnalyticsController =
        AnalyticsController(channelsApi = channelsApi, analyticsApi = analyticsApi)

    val rewardsController: RewardsController =
        RewardsController(channelsApi = channelsApi, rewardsApi = rewardsApi)

    val songRequestsController: SongRequestsController =
        SongRequestsController(channelsApi = channelsApi, songRequestsApi = songRequestsApi)

    val ttsController: TtsController = TtsController(channelsApi = channelsApi, ttsApi = ttsApi)
    val ttsQueueController: TtsQueueController =
        TtsQueueController(channelsApi = channelsApi, ttsApi = ttsApi)

    val gamesController: GamesController =
        GamesController(channelsApi = channelsApi, gamesApi = gamesApi)

    val eventResponsesController: EventResponsesController =
        EventResponsesController(channelsApi = channelsApi, eventResponsesApi = eventResponsesApi)

    val settingsController: SettingsController =
        SettingsController(channelsApi = channelsApi, streamApi = streamApi)

    val personalityController: PersonalityController =
        PersonalityController(channelsApi = channelsApi, settingsApi = channelSettingsApi)

    val engagementController: EngagementController = EngagementController(api = engagementApi)

    val channelBotController: ChannelBotController = ChannelBotController(channelsApi = channelsApi)

    val billingController: BillingController =
        BillingController(channelsApi = channelsApi, billingApi = billingApi)

    val adminController: AdminController =
        AdminController(
            api = adminApi,
            iamApi = platformIamApi,
            platformAdminApi = platformAdminApi,
            hubClient = adminHubClient,
            baseUrl = sessionStore::baseUrl,
            accessToken = sessionStore::accessToken,
            refreshToken = tokenRefresher,
        )

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
        WidgetsController(
            channelsApi = channelsApi,
            widgetsApi = widgetsApi,
            widgetGalleryApi = widgetGalleryApi,
            projectEditor = projectEditor,
        )

    val chatController: ChatController =
        ChatController(channelsApi = channelsApi, chatApi = chatApi)

    val quotesController: QuotesController =
        QuotesController(quotesApi = quotesApi, feedback = feedbackController)

    val pickListsController: PickListsController =
        PickListsController(pickListsApi = pickListsApi, feedback = feedbackController)

    val giveawaysController: GiveawaysController =
        GiveawaysController(giveawaysApi = giveawaysApi, feedback = feedbackController)

    val supportersController: SupportersController =
        SupportersController(supportersApi = supportersApi, feedback = feedbackController)

    val soundController: SoundController =
        SoundController(
            soundApi = soundApi,
            audioPicker = AudioFilePicker(),
            feedback = feedbackController,
        )

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
        PipelinesController(
            channelsApi = channelsApi,
            pipelinesApi = pipelinesApi,
            webhooksApi = webhooksApi,
            pickListsApi = pickListsApi,
            feedback = feedbackController,
        )

    val featuresController: FeaturesController =
        FeaturesController(channelsApi = channelsApi, featuresApi = featuresApi)

    val webhooksController: WebhooksController =
        WebhooksController(channelsApi = channelsApi, webhooksApi = webhooksApi)

    val customEventsController: CustomEventsController =
        CustomEventsController(api = customEventsApi)

    val federationController: FederationController =
        FederationController(channelsApi = channelsApi, federationApi = federationApi)

    val codeScriptsController: CodeScriptsController =
        CodeScriptsController(api = codeScriptsApi, projectEditor = projectEditor)

    val liveOpsController: LiveOpsController =
        LiveOpsController(channelsApi = channelsApi, liveOpsApi = liveOpsApi)

    val scheduleController: ScheduleController =
        ScheduleController(channelsApi = channelsApi, liveOpsApi = liveOpsApi)

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
            systemApi = systemApi,
            analyticsApi = analyticsApi,
            pronounsApi = pronounsApi,
        )
}
