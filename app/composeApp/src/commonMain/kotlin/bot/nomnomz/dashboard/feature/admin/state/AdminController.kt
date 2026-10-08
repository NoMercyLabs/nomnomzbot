// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.feature.shell.state.ActAsCoordinator
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.SpamDefenseApi
import bot.nomnomz.dashboard.core.network.PaginatedEnvelope
import bot.nomnomz.dashboard.core.network.TenantMember
import bot.nomnomz.dashboard.core.network.TenantMembersApi
import bot.nomnomz.dashboard.core.network.SpamDefensePolicy
import bot.nomnomz.dashboard.core.network.SpamDefenseSettings
import bot.nomnomz.dashboard.core.network.AdminApi
import bot.nomnomz.dashboard.core.network.AdminChannel
import bot.nomnomz.dashboard.core.network.AdminCreateInviteCodeRequest
import bot.nomnomz.dashboard.core.network.AdminAuthorPricedUnitRequest
import bot.nomnomz.dashboard.core.network.AdminGrantTierRequest
import bot.nomnomz.dashboard.core.network.AdminPricedUnit
import bot.nomnomz.dashboard.core.network.AdminServiceHealth
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagOverrideRequest
import bot.nomnomz.dashboard.core.network.AdminSetFeatureFlagRequest
import bot.nomnomz.dashboard.core.network.ProviderCredential
import bot.nomnomz.dashboard.core.network.SaveProviderCredentialBody
import bot.nomnomz.dashboard.core.network.AdminCreateTierRequest
import bot.nomnomz.dashboard.core.network.AdminStats
import bot.nomnomz.dashboard.core.network.AdminSystem
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminEventReplayPreview
import bot.nomnomz.dashboard.core.network.AdminEventReplayResult
import bot.nomnomz.dashboard.core.network.AdminInvoice
import bot.nomnomz.dashboard.core.network.AdminReplayableProjection
import bot.nomnomz.dashboard.core.network.AdminScheduledJob
import bot.nomnomz.dashboard.core.network.AdminTenantErrorBudget
import bot.nomnomz.dashboard.core.network.AdminTenantUsage
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.core.network.AdminTierChangePreview
import bot.nomnomz.dashboard.core.network.AdminUpdateTierRequest
import bot.nomnomz.dashboard.core.network.AdminUser
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AssignRoleBody
import bot.nomnomz.dashboard.core.network.BeginTenantAccessBody
import bot.nomnomz.dashboard.core.network.CreatePrincipalBody
import bot.nomnomz.dashboard.core.network.FeatureFlag
import bot.nomnomz.dashboard.core.network.FeatureFlagBlastRadiusDto
import bot.nomnomz.dashboard.core.network.FeatureFlagOverride
import bot.nomnomz.dashboard.core.network.IamAuditEntry
import bot.nomnomz.dashboard.core.network.IamPrincipal
import bot.nomnomz.dashboard.core.network.IamRoleAssignment
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.core.network.ImpersonationTokenDto
import bot.nomnomz.dashboard.core.network.InviteCode
import bot.nomnomz.dashboard.core.network.CreateContentDefinitionBody
import bot.nomnomz.dashboard.core.network.DraftContentVersionBody
import bot.nomnomz.dashboard.core.network.AdminSupportApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.PlatformAdminApi
import bot.nomnomz.dashboard.core.network.SupportPersonHistoryEntry
import bot.nomnomz.dashboard.core.network.SupportPersonSearchResult
import bot.nomnomz.dashboard.core.network.SupportPersonView
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseSignal
import bot.nomnomz.dashboard.core.network.DeviceBotPoll
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.NetworkBlock
import bot.nomnomz.dashboard.core.network.NetworkBlockPreview
import bot.nomnomz.dashboard.core.network.PlatformBotAdminApi
import bot.nomnomz.dashboard.core.network.PlatformBotAdminState
import bot.nomnomz.dashboard.core.network.PlatformBotAdminStatus
import bot.nomnomz.dashboard.core.network.PlatformBotReconnectPreview
import bot.nomnomz.dashboard.core.network.TrustSafetyApi
import bot.nomnomz.dashboard.core.network.ComplianceApi
import bot.nomnomz.dashboard.core.network.ErasurePreview
import bot.nomnomz.dashboard.core.network.ErasureRequest
import bot.nomnomz.dashboard.core.network.ErasureRequestSummary
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import bot.nomnomz.dashboard.core.network.PlatformContentApi
import bot.nomnomz.dashboard.core.network.PlatformContentDefinition
import bot.nomnomz.dashboard.core.network.PlatformContentDefinitionDetail
import bot.nomnomz.dashboard.core.network.PlatformContentPublishJob
import bot.nomnomz.dashboard.core.network.PlatformContentPublishModes
import bot.nomnomz.dashboard.core.network.PlatformEvent
import bot.nomnomz.dashboard.core.network.PublishContentBody
import bot.nomnomz.dashboard.core.network.PublishPreview
import bot.nomnomz.dashboard.core.network.PublishPreviewBody
import bot.nomnomz.dashboard.core.network.PlatformIamApi
import bot.nomnomz.dashboard.core.network.ReinstateTenantBody
import bot.nomnomz.dashboard.core.network.SuspendTenantBody
import bot.nomnomz.dashboard.core.network.TenantAccessGrant
import bot.nomnomz.dashboard.core.realtime.AdminHubClient
import bot.nomnomz.dashboard.core.realtime.AdminHubEvent
import bot.nomnomz.dashboard.core.realtime.AdminLogEntry
import bot.nomnomz.dashboard.core.realtime.AdminRegistryUpdate
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_act_as_unavailable
import nomnomzbot.composeapp.generated.resources.admin_action_error
import nomnomzbot.composeapp.generated.resources.admin_flag_override_no_channel
import nomnomzbot.composeapp.generated.resources.admin_job_retry_scheduled
import nomnomzbot.composeapp.generated.resources.admin_network_block_applied
import nomnomzbot.composeapp.generated.resources.admin_review_confirmed
import nomnomzbot.composeapp.generated.resources.admin_review_overturned

/**
 * The ordering keys the admin lists send. The server parses exactly these and falls back to its default
 * for anything else, so they live in one place rather than as string literals in the screen.
 */
object AdminSort {
    const val Newest: String = "newest"
    const val Oldest: String = "oldest"
    const val Name: String = "name"
}

data class AdminState(
    val stats: AdminStats? = null,
    /** Platform-wide spam-defence defaults; null until the tab is first opened, or if the read failed. */
    val spamDefaults: SpamDefensePolicy? = null,
    val channels: List<AdminChannel> = emptyList(),
    val channelSearch: String = "",
    /** 1-based page the channel list is currently showing. */
    val channelPage: Int = 1,
    /** Whether the server said there is a page after [channelPage] — the only honest basis for a Next control. */
    val channelHasMore: Boolean = false,
    /** Ordering key sent to the server: newest (default) / oldest / name. */
    val channelSort: String = AdminSort.Newest,
    /** Live filter: null = both, true = only live, false = only offline. */
    val channelLiveFilter: Boolean? = null,
    val users: List<AdminUser> = emptyList(),
    val userSearch: String = "",
    /** 1-based page the user list is currently showing. */
    val userPage: Int = 1,
    /** Whether the server said there is a page after [userPage]. */
    val userHasMore: Boolean = false,
    /** Ordering key sent to the server: newest (default) / oldest / name. */
    val userSort: String = AdminSort.Newest,
    /** Role filter: null = everyone, "admin" = platform staff, "user" = everyone else. */
    val userRoleFilter: String? = null,
    val system: AdminSystem? = null,
    val health: List<AdminServiceHealth> = emptyList(),
    val events: List<PlatformEvent> = emptyList(),
    val featureFlags: List<FeatureFlag> = emptyList(),
    /** The flag key a global-toggle kill-switch confirmation is currently open for, or null when no confirm
     * dialog is showing. Set as soon as the operator flips a flag from on to off, cleared on confirm/dismiss. */
    val flagKillSwitchKey: String? = null,
    /** The counted blast radius for [flagKillSwitchKey], once its preview call returns; null while loading. */
    val flagKillSwitchPreview: FeatureFlagBlastRadiusDto? = null,
    /** Every per-tenant override on every flag, read back from the server with the flags (never inferred from a write). */
    val flagOverrides: List<FeatureFlagOverride> = emptyList(),
    val inviteCodes: List<InviteCode> = emptyList(),
    // ── Tier authoring (S-ADMIN-4a) ──
    val tiers: List<AdminTier> = emptyList(),
    /** The id of the tier currently open in the edit dialog, or null when it is closed. Set the moment the
     * operator opens a tier for editing — its counted blast-radius preview is fetched immediately after. */
    val tierEditId: String? = null,
    /** The counted blast radius for [tierEditId], once its preview call returns; null while loading. */
    val tierEditPreview: AdminTierChangePreview? = null,
    // ── Priced-unit authoring (S-ADMIN-4d) ──
    val pricedUnits: List<AdminPricedUnit> = emptyList(),
    /** True while the author-a-price dialog is open (create or reprice); false when closed. */
    val pricedUnitFormOpen: Boolean = false,
    /** The unit key the form was opened to REPRICE, or null when it is authoring a brand-new unit key. */
    val pricedUnitFormEditingKey: String? = null,
    // ── Comps and entitlement grants (S-ADMIN-4b) ──
    /** The broadcaster the grant panel is currently looking at, or null before the operator picks one. */
    val grantBroadcasterId: String? = null,
    /** Every LIVE grant for [grantBroadcasterId], newest first. */
    val entitlementGrants: List<AdminEntitlementGrant> = emptyList(),
    /** The counted blast radius for the tier currently staged to comp [grantBroadcasterId] to; null while
     * loading, and cleared whenever the staged tier changes so a stale count can never be confirmed against. */
    val entitlementGrantPreview: AdminEntitlementGrantPreview? = null,
    // ── Invoices, dunning and refunds (S-ADMIN-4c) ──
    /** The broadcaster the invoices panel is currently looking at, or null before the operator picks one. */
    val invoiceBroadcasterId: String? = null,
    /** Every invoice for [invoiceBroadcasterId], newest first, with its live dunning state. */
    val invoices: List<AdminInvoice> = emptyList(),
    /** The invoice a refund confirmation is currently open for, or null when no confirm dialog is showing —
     * the amount and currency shown in that dialog come straight off this row (already the counted blast
     * radius: a refund touches exactly this one invoice's paid amount, never more). */
    val refundPendingInvoiceId: String? = null,
    /** Which of the tabs fed by [AdminController.load] are currently (re)fetching — per-tab, not whole-screen, so
     * a slow call on one tab never blocks the others or the tab bar. Empty once the initial load settles. */
    val loadingSections: Set<AdminSection> = emptySet(),
    val error: String? = null,
    /** True when the server answered 403 to the admin reads: signed in, not allowed. The screen shows access denied. */
    val accessDenied: Boolean = false,
    // ── Live operator-hub feed (AdminHub) ──
    /** True once the AdminHub WebSocket handshake completes — drives the "live" indicator (not polling). */
    val hubLive: Boolean = false,
    /** Live channel-registry rows keyed by broadcasterId, most-recently-updated first. */
    val registry: List<AdminRegistryUpdate> = emptyList(),
    /** Live operator log lines, newest first (capped). */
    val logs: List<AdminLogEntry> = emptyList(),
    // ── IAM ──
    val roles: List<IamRole> = emptyList(),
    val principals: List<IamPrincipalSummary> = emptyList(),
    val iamLoading: Boolean = false,
    val iamError: String? = null,
    /** Effective permission keys for the last-inspected principal (principalId → keys). */
    val effectivePermissions: Map<String, List<String>> = emptyMap(),
    /** A service-account key returned exactly once on creation — shown, then cleared. */
    val issuedServiceAccountKey: String? = null,
    // ── Tenants ──
    val tenants: List<AdminTenant> = emptyList(),
    val tenantSearch: String = "",
    val tenantStatusFilter: String? = null,
    val tenantsLoading: Boolean = false,
    val tenantsPage: Int = 1,
    val tenantsHasMore: Boolean = false,
    val tenantsError: String? = null,
    val selectedTenant: AdminTenantDetail? = null,
    /** The mandatory, audited reason for browsing one tenant's own custom content (support desk key). */
    val tenantContentJustification: String = "",
    /** The broadcaster id whose own commands/pipelines are currently shown, or null when the panel is closed. */
    val tenantContentOpenFor: String? = null,
    val tenantContentLoading: Boolean = false,
    val tenantContentError: String? = null,
    /** The tenant's own custom (non-platform) commands — investigation-only, never edited from here. */
    val tenantCommands: List<CommandSummary> = emptyList(),
    /** The tenant's own custom (non-platform-sourced) pipelines — investigation-only, never edited from here. */
    val tenantPipelines: List<PipelineSummary> = emptyList(),
    // ── Provider app credentials ──
    /** One row per provider the build supports; empty until the Providers tab is first opened. */
    val providerCredentials: List<ProviderCredential> = emptyList(),
    val providersLoading: Boolean = false,
    val providersError: String? = null,
    // ── Audit ──
    val auditEntries: List<IamAuditEntry> = emptyList(),
    val auditOutcomeFilter: String? = null,
    val auditPermissionFilter: String = "",
    val auditLoading: Boolean = false,
    val auditPage: Int = 1,
    val auditHasMore: Boolean = false,
    val auditError: String? = null,
    // ── EventSub subscription health (S-ADMIN-6a) ──
    val eventSubHealth: List<AdminEventSubTenantHealth> = emptyList(),
    val eventSubHealthLoading: Boolean = false,
    val eventSubHealthError: String? = null,
    /** 1-based page the EventSub health list is currently showing. */
    val eventSubHealthPage: Int = 1,
    /** Whether the server said there is a page after [eventSubHealthPage]. */
    val eventSubHealthHasMore: Boolean = false,
    // ── Outbound webhook delivery log + replay (S-ADMIN-6a) ──
    val webhookDeliveries: List<AdminWebhookDelivery> = emptyList(),
    val webhookDeliveriesLoading: Boolean = false,
    val webhookDeliveriesError: String? = null,
    /** 1-based page the webhook delivery log is currently showing. */
    val webhookDeliveriesPage: Int = 1,
    /** Whether the server said there is a page after [webhookDeliveriesPage]. */
    val webhookDeliveriesHasMore: Boolean = false,
    /** The delivery a replay confirmation is currently open for, or null when no confirm dialog is showing —
     * what it will re-send (event type + target endpoint) comes straight off this row. */
    val replayPendingDeliveryId: Long? = null,
    // ── Background job queue + retry (S-ADMIN-6b) ──
    val scheduledJobs: List<AdminScheduledJob> = emptyList(),
    val scheduledJobsLoading: Boolean = false,
    val scheduledJobsError: String? = null,
    /** 1-based page the background job queue is currently showing. */
    val scheduledJobsPage: Int = 1,
    /** Whether the server said there is a page after [scheduledJobsPage]. */
    val scheduledJobsHasMore: Boolean = false,
    /** The job a retry confirmation is currently open for, or null when no confirm dialog is showing — the
     * pipeline it will re-run comes straight off this row (consequences must be visible before it commits). */
    val retryPendingJobId: String? = null,
    // ── Per-tenant usage (S-ADMIN-6b) ──
    val tenantUsage: List<AdminTenantUsage> = emptyList(),
    val tenantUsageLoading: Boolean = false,
    val tenantUsageError: String? = null,
    /** 1-based page the per-tenant usage list is currently showing. */
    val tenantUsagePage: Int = 1,
    /** Whether the server said there is a page after [tenantUsagePage]. */
    val tenantUsageHasMore: Boolean = false,
    // ── Error budget (S-ADMIN-6c) ──
    val errorBudget: List<AdminTenantErrorBudget> = emptyList(),
    val errorBudgetLoading: Boolean = false,
    val errorBudgetError: String? = null,
    /** 1-based page the error budget list is currently showing. */
    val errorBudgetPage: Int = 1,
    /** Whether the server said there is a page after [errorBudgetPage]. */
    val errorBudgetHasMore: Boolean = false,
    // ── Event-store replay (S-ADMIN-6c) ──
    val replayProjections: List<AdminReplayableProjection> = emptyList(),
    val replayProjectionsLoading: Boolean = false,
    val replayProjectionsError: String? = null,
    /** The operator's in-progress scope selection — tenant + projection + window + optional event type. */
    val replayProjectionName: String = "",
    val replayBroadcasterId: String = "",
    val replayFromUtc: String = "",
    val replayToUtc: String = "",
    val replayEventType: String = "",
    /** The REAL counted preview for the current scope. Null until a preview succeeds, and cleared on every
     * scope edit — a stale preview can never be confirmed against inputs the operator has since changed. */
    val replayPreview: AdminEventReplayPreview? = null,
    val replayPreviewLoading: Boolean = false,
    val replayPreviewError: String? = null,
    /** True only while the replay confirm dialog is open — reachable only once [replayPreview] is real. */
    val replayConfirmOpen: Boolean = false,
    val replayExecuting: Boolean = false,
    val replayResult: AdminEventReplayResult? = null,
    // ── Impersonation (admin act-as) ──
    /** Set alongside the feedback toast when a mint attempt fails for one of these two RECOGNIZED reasons, so
     * the confirm dialog can render a calm, specific explanation instead of the raw server message. Null for
     * any other failure (network error, unexpected 5xx, …) — those still surface via the feedback toast alone. */
    val impersonationRefusal: ImpersonationRefusal? = null,
    /** True while an act-as begin is in flight — the confirm action is disabled so a double click never opens
     * two support sessions. */
    val impersonationInFlight: Boolean = false,
    /** Set when the act-as picker could not load the tenant's people — an empty list and a failed load must
     * never look the same. */
    val tenantMembersError: String? = null,
    // ── Platform content authoring (S-ADMIN-2b) ──
    val contentDefinitions: List<PlatformContentDefinition> = emptyList(),
    /** The channel event types an `event_response` template can target (the server's event catalogue). */
    val contentEventResponseTypes: List<String> = emptyList(),
    val contentLoading: Boolean = false,
    val contentError: String? = null,
    /** The definition currently open in the editor, incl. its full version history. Null when the list is
     * showing and nothing is open yet. */
    val selectedContentDefinition: PlatformContentDefinitionDetail? = null,
    val contentDetailLoading: Boolean = false,
    val contentDetailError: String? = null,
    /** The blast-radius preview for the version+mode combination currently staged for publish — the real
     * counted answer from `publish-preview`, never a guess. Cleared whenever the definition, version, or
     * mode selection changes so a stale count can never be confirmed against. */
    val publishPreview: PublishPreview? = null,
    val publishPreviewLoading: Boolean = false,
    val publishPreviewError: String? = null,
    val publishSubmitting: Boolean = false,
    /** The completed publish job, shown as confirmation once a publish commits. */
    val lastPublishJob: PlatformContentPublishJob? = null,
    // ── Cross-tenant support desk (S-ADMIN-7a) ──
    /** The name/platform-id the operator last searched for across every tenant. */
    val supportSearch: String = "",
    /** The mandatory, audited reason the operator gave for looking. Never blank when a call is made. */
    val supportJustification: String = "",
    val supportResults: List<SupportPersonSearchResult> = emptyList(),
    /** True once a search has actually run, so an empty list reads as "no matches" rather than "not searched". */
    val supportSearched: Boolean = false,
    val supportLoading: Boolean = false,
    val supportError: String? = null,
    /** The person currently open. Every list on it is REAL — an empty one means the system holds no such
     * fact for this person, and the UI omits that block instead of rendering a zeroed row. */
    val supportPerson: SupportPersonView? = null,
    val supportPersonLoading: Boolean = false,
    val supportPersonError: String? = null,
    /** What actually happened to this person, replayed from the real event journal (S-ADMIN-7b) — newest
     * first, each entry naming the tenant it happened in. [supportHistoryLoaded] tells an empty list (no
     * recorded history) apart from "never fetched", so the empty state never reads as an error. */
    val supportHistory: List<SupportPersonHistoryEntry> = emptyList(),
    val supportHistoryLoaded: Boolean = false,
    val supportHistoryLoading: Boolean = false,
    val supportHistoryError: String? = null,
    // ── Platform-wide trust & safety desk (S-ADMIN-8a) ──
    /** The mandatory, audited reason for a cross-tenant read or an overturn. Never blank when a call is made. */
    val trustSafetyJustification: String = "",
    val crossTenantSignals: List<CrossTenantAbuseSignal> = emptyList(),
    /** True once the correlation has actually run, so an empty list reads as "none found" not "not run". */
    val crossTenantSignalsLoaded: Boolean = false,
    val crossTenantSignalsLoading: Boolean = false,
    val crossTenantSignalsError: String? = null,
    val reviewQueue: List<TrustSafetyReviewItem> = emptyList(),
    val reviewQueueLoaded: Boolean = false,
    val reviewQueueLoading: Boolean = false,
    val reviewQueueError: String? = null,
    /** The row an overturn confirm dialog is open for — its [TrustSafetyReviewItem.reversalPreview] is the
     * message the dialog shows, so the operator sees the blast radius before it commits. */
    val reviewItemPendingOverturn: TrustSafetyReviewItem? = null,
    val reviewActionInFlight: String? = null,
    // ── GDPR data requests, platform-wide (A7) ──
    /** Every subject's requests matching the two filters, newest first; empty until the tab is first opened. */
    val dataRequests: List<ErasureRequest> = emptyList(),
    /** Counts by status from the real ledger; null until the tab is first opened. */
    val dataRequestSummary: ErasureRequestSummary? = null,
    val dataRequestStatusFilter: String? = null,
    val dataRequestTypeFilter: String? = null,
    val dataRequestsLoading: Boolean = false,
    val dataRequestsError: String? = null,
    /** The failed erasure a "Re-run erasure" confirm dialog is open for; null when no dialog is showing. */
    val erasureRetryTarget: ErasureRequest? = null,
    /** The subject's counted blast radius for [erasureRetryTarget]; null while it loads or when it failed. */
    val erasureRetryPreview: ErasurePreview? = null,
    /** True when the blast-radius lookup failed — kept apart from a genuine zero, and it withholds the confirm. */
    val erasureRetryPreviewFailed: Boolean = false,
    val erasureRetryRunning: Boolean = false,
    /** Why the last re-run was refused or failed; the dialog stays open and shows it. */
    val erasureRetryError: ApiError? = null,
    // ── Network-wide block (S-ADMIN-8b) ──
    /** The Twitch user id the operator is about to preview/block — free text, not resolved until preview. */
    val networkBlockTargetTwitchUserId: String = "",
    val networkBlockReason: String = "",
    /** The real, freshly-computed blast radius — required before [AdminController.requestApplyNetworkBlock]
     * will open the confirm dialog. Cleared whenever the target or justification changes, so a stale
     * preview can never be applied against a number the operator did not just see. */
    val networkBlockPreview: NetworkBlockPreview? = null,
    val networkBlockPreviewLoading: Boolean = false,
    val networkBlockPreviewError: String? = null,
    /** True while the destructive confirm dialog (showing the counted blast radius) is open. */
    val networkBlockApplyConfirmOpen: Boolean = false,
    val networkBlockApplyInFlight: Boolean = false,
    val networkBlocks: List<NetworkBlock> = emptyList(),
    val networkBlocksLoaded: Boolean = false,
    val networkBlocksLoading: Boolean = false,
    val networkBlocksError: String? = null,
    val networkBlockLiftInFlight: String? = null,

    // ── Shared platform bot (S-BOT-PLATFORM-UI, Setup) ──
    val platformBotStatus: PlatformBotAdminStatus? = null,
    val platformBotStatusLoading: Boolean = false,
    val platformBotStatusError: String? = null,
    val platformBotJustification: String = "",
    /** The real, freshly-computed blast radius — required before [AdminController.requestPlatformBotReconnect]
     * will open the confirm dialog. Cleared whenever the justification changes, so a stale preview can
     * never be applied against a number the operator did not just see. */
    val platformBotReconnectPreview: PlatformBotReconnectPreview? = null,
    val platformBotReconnectPreviewLoading: Boolean = false,
    val platformBotReconnectPreviewError: String? = null,
    /** True while the destructive confirm dialog (showing the counted blast radius) is open. */
    val platformBotReconnectConfirmOpen: Boolean = false,
    /** The device code the operator is approving at twitch.tv/activate, once the reconnect has started. */
    val platformBotReconnectDevice: PlatformBotReconnectDeviceState? = null,
)

/** The device-code panel shown while a platform-bot reconnect awaits the operator's twitch.tv approval. */
data class PlatformBotReconnectDeviceState(val userCode: String, val verificationUri: String)

/** One tab whose data comes from [AdminController.load]/[AdminController.loadChannels]/[AdminController.loadUsers]
 * — tracked per-tab in [AdminState.loadingSections] so a slow fetch on one tab never spinners the others. */
enum class AdminSection {
    Overview,
    Channels,
    Users,
    System,
    FeatureFlags,
    Billing,
}

/** The refusals the impersonation confirm dialog explains specifically, keyed on the server's error codes. */
enum class ImpersonationRefusal {
    /** No open, audited support session for this tenant — minting is refused until one is begun. */
    NoOpenSupportSession,
    /** The caller's role does not carry the impersonation grant (platform-owner role only). */
    NotPermitted,
    /** The chosen person does not belong to the tenant the support session covers. */
    TargetOutsideSession,
}

/** A channel an operator named, by the id the API needs; its display name and login render the label. */
data class ResolvedChannel(val id: String, val displayName: String?, val login: String?)

private val CHANNEL_ID: Regex = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

/** The server's fail-closed code when a confirmed blast-radius count no longer matches a fresh one. */
private const val PREVIEW_STALE: String = "PREVIEW_STALE"

/**
 * The platform-admin panel's holder. Beyond the read-only stats/channels/users/flags/billing it drives the
 * Plane-C management surfaces — IAM (principals/roles), tenant operations (suspend/reinstate/detail), and the
 * audit log — and subscribes to the live [AdminHubClient] so the home status panel + channel registry move
 * without polling. Every write re-loads its slice so the UI reflects the persisted truth after reload.
 */
class AdminController(
    private val api: AdminApi,
    // Platform-wide spam-defence defaults. Nullable like the other optional collaborators so a bare
    // test controller still builds; the tab then stays hidden rather than rendering defaults nobody read.
    private val spamDefenseApi: SpamDefenseApi? = null,
    private val iamApi: PlatformIamApi,
    private val platformAdminApi: PlatformAdminApi,
    // The cross-tenant support desk. Nullable like the other optional collaborators so a bare test
    // controller still builds; the tab stays hidden when the build has no client for it.
    private val supportApi: AdminSupportApi? = null,
    // The platform-wide trust & safety desk (S-ADMIN-8a). Nullable for the same reason: a bare test
    // controller still builds, and the tab stays hidden when the build has no client for it.
    private val trustSafetyApi: TrustSafetyApi? = null,
    // The platform-wide GDPR request monitor (A7). Nullable for the same reason: a bare test controller
    // still builds, and the tab stays hidden when the build has no client for it.
    private val complianceApi: ComplianceApi? = null,
    // The shared platform bot's admin surface (S-BOT-PLATFORM-UI). Nullable like the other optional
    // collaborators so a bare test controller still builds; the tab stays hidden when the build has no
    // client for it.
    private val platformBotAdminApi: PlatformBotAdminApi? = null,
    private val contentApi: PlatformContentApi? = null,
    private val hubClient: AdminHubClient? = null,
    private val baseUrl: () -> String? = { null },
    private val accessToken: () -> String? = { null },
    private val refreshToken: (suspend () -> Boolean)? = null,
    // The signed-in operator's session (for [currentUserId]) and the act-as lifecycle. A controller built without
    // [actAs] refuses to begin act-as loudly rather than half-swapping the session.
    private val sessionStore: SessionStore? = null,
    private val actAs: ActAsCoordinator? = null,
    private val tenantMembersApi: TenantMembersApi? = null,
    private val feedback: Feedback = NoOpFeedback,
) {
    /** The signed-in operator's own user id (for gating the Users tab so it never offers "act as yourself"). */
    val currentUserId: String?
        get() = sessionStore?.user?.value?.id
    private val _state: MutableStateFlow<AdminState> = MutableStateFlow(AdminState())
    val state: StateFlow<AdminState> = _state.asStateFlow()

    /** The live operator-hub event stream, for the screen to collect via [subscribeToHub]. */
    val hubEvents: SharedFlow<AdminHubEvent>? = hubClient?.events

    suspend fun load() {
        _state.value = _state.value.copy(loadingSections = LOAD_SECTIONS, error = null)

        fetchSnapshot()

        // A 403 is "signed in, not allowed": no hub, no refresh, just the access-denied state.
        if (_state.value.accessDenied) return

        // Connect the operator hub once — the handshake is gated on the caller's iam:manage grant, so a
        // non-privileged admin simply never establishes and the panel falls back to the REST snapshot.
        val url: String? = baseUrl()
        if (hubClient != null && url != null) {
            hubClient.connect(url, accessToken, refreshToken)
        }
    }

    /**
     * Re-fetches every section the snapshot carries. The channel and user lists keep their current search,
     * sort, filter and page — a write's refresh must not silently widen them while the chips still read as
     * applied — and the tab spinners are never flipped here: a spinner unmounts the tab that launched the
     * write, which cancels this very refresh mid-flight and leaves the lists empty.
     */
    private suspend fun fetchSnapshot() {
        val current: AdminState = _state.value
        val statsResult = api.getStats()
        val channelsResult = api.getChannels(
            search = current.channelSearch,
            page = current.channelPage,
            sort = current.channelSort,
            isLive = current.channelLiveFilter,
        )
        val usersResult = api.getUsers(
            search = current.userSearch,
            page = current.userPage,
            sort = current.userSort,
            role = current.userRoleFilter,
        )
        val systemResult = api.getSystem()
        val healthResult = api.getHealth()
        val eventsResult = api.getEvents()
        val flagsResult = api.getFeatureFlags()
        val flagOverridesResult = api.getFeatureFlagOverrides()
        val invitesResult = api.getInviteCodes()
        val tiersResult = api.getTiers()
        val pricedUnitsResult = api.getPricedUnits()

        _state.value = _state.value.copy(
            stats = (statsResult as? ApiResult.Ok)?.value ?: _state.value.stats,
            channels = (channelsResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            channelHasMore = (channelsResult as? ApiResult.Ok)?.value?.hasMore ?: false,
            users = (usersResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            userHasMore = (usersResult as? ApiResult.Ok)?.value?.hasMore ?: false,
            system = (systemResult as? ApiResult.Ok)?.value ?: _state.value.system,
            health = (healthResult as? ApiResult.Ok)?.value ?: emptyList(),
            events = (eventsResult as? ApiResult.Ok)?.value ?: emptyList(),
            featureFlags = (flagsResult as? ApiResult.Ok)?.value ?: emptyList(),
            flagOverrides = (flagOverridesResult as? ApiResult.Ok)?.value ?: emptyList(),
            inviteCodes = (invitesResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            tiers = (tiersResult as? ApiResult.Ok)?.value ?: emptyList(),
            pricedUnits = (pricedUnitsResult as? ApiResult.Ok)?.value ?: emptyList(),
            loadingSections = emptySet(),
            accessDenied = listOf(statsResult, channelsResult, usersResult, systemResult).all {
                it is ApiResult.Failure && it.error.status == 403
            },
            error = listOf(statsResult, channelsResult, usersResult, systemResult)
                .filterIsInstance<ApiResult.Failure>()
                .firstOrNull()
                ?.error
                ?.message,
        )
    }

    /** Re-fetches the channel list, narrowed by [search] against the channel's login or its owner's display
     * name — the last-submitted search when [search] is omitted, so a page-size/status change re-applies it. */
    /**
     * Re-fetches the channel list. A new [search] resets to page 1 — staying on page 4 of the previous
     * query would show an empty list and read as "no matches" for a search that has plenty.
     */
    suspend fun loadChannels(
        search: String? = null,
        page: Int? = null,
        sort: String? = null,
        isLive: Boolean? = null,
        clearLiveFilter: Boolean = false,
    ) {
        val effectiveSearch: String = search ?: _state.value.channelSearch
        val effectiveSort: String = sort ?: _state.value.channelSort
        val effectiveLive: Boolean? =
            if (clearLiveFilter) null else isLive ?: _state.value.channelLiveFilter
        // Any change to WHICH rows are being listed returns to page 1 — a narrowed list read from page 4
        // is an empty screen that looks like "no results".
        val narrowed: Boolean = search != null || sort != null || isLive != null || clearLiveFilter
        val effectivePage: Int = page ?: if (narrowed) 1 else _state.value.channelPage
        _state.value = _state.value.copy(
            channelSearch = effectiveSearch,
            channelSort = effectiveSort,
            channelLiveFilter = effectiveLive,
            channelPage = effectivePage,
            loadingSections = _state.value.loadingSections + AdminSection.Channels,
        )
        when (
            val result = api.getChannels(
                search = effectiveSearch,
                page = effectivePage,
                sort = effectiveSort,
                isLive = effectiveLive,
            )
        ) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    channels = result.value.data,
                    channelHasMore = result.value.hasMore,
                )
            is ApiResult.Failure -> _state.value = _state.value.copy(error = result.error.message)
        }
        _state.value = _state.value.copy(loadingSections = _state.value.loadingSections - AdminSection.Channels)
    }

    /** Re-fetches the user list, narrowed by [search] against the user's login or display name — the
     * last-submitted search when [search] is omitted. */
    suspend fun loadUsers(
        search: String? = null,
        page: Int? = null,
        sort: String? = null,
        role: String? = null,
        clearRoleFilter: Boolean = false,
    ) {
        val effectiveSearch: String = search ?: _state.value.userSearch
        val effectiveSort: String = sort ?: _state.value.userSort
        val effectiveRole: String? =
            if (clearRoleFilter) null else role ?: _state.value.userRoleFilter
        val narrowed: Boolean = search != null || sort != null || role != null || clearRoleFilter
        val effectivePage: Int = page ?: if (narrowed) 1 else _state.value.userPage
        _state.value = _state.value.copy(
            userSearch = effectiveSearch,
            userSort = effectiveSort,
            userRoleFilter = effectiveRole,
            userPage = effectivePage,
            loadingSections = _state.value.loadingSections + AdminSection.Users,
        )
        when (
            val result = api.getUsers(
                search = effectiveSearch,
                page = effectivePage,
                sort = effectiveSort,
                role = effectiveRole,
            )
        ) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    users = result.value.data,
                    userHasMore = result.value.hasMore,
                )
            is ApiResult.Failure -> _state.value = _state.value.copy(error = result.error.message)
        }
        _state.value = _state.value.copy(loadingSections = _state.value.loadingSections - AdminSection.Users)
    }

    /**
     * Fold live operator-hub pushes into state: the 15 s system heartbeat overwrites the health/stats panel,
     * a registry update lands in the live channel-registry list, and a log push prepends the operator log.
     */
    suspend fun subscribeToHub(events: SharedFlow<AdminHubEvent>) {
        events.collect { evt ->
            val current: AdminState = _state.value
            when (evt) {
                is AdminHubEvent.SystemStatus ->
                    _state.value = current.copy(
                        hubLive = true,
                        system = evt.system ?: current.system,
                        stats = evt.stats ?: current.stats,
                    )
                is AdminHubEvent.RegistryUpdate -> {
                    val without: List<AdminRegistryUpdate> =
                        current.registry.filterNot { it.broadcasterId == evt.update.broadcasterId }
                    _state.value = current.copy(
                        hubLive = true,
                        registry = (listOf(evt.update) + without).take(REGISTRY_CAP),
                    )
                }
                is AdminHubEvent.Log ->
                    _state.value = current.copy(
                        hubLive = true,
                        logs = (listOf(evt.entry) + current.logs).take(LOG_CAP),
                    )
                is AdminHubEvent.Unknown -> Unit
                AdminHubEvent.Disconnected -> _state.value = current.copy(hubLive = false)
            }
        }
    }

    // ── Feature flags & billing (write, then reload) ──────────────────────────

    /**
     * Runs one admin write and reloads on success, surfacing a failure on the shell-level feedback toast the
     * way the IAM actions below already do. Every write in this block used to discard its [ApiResult] outright,
     * so a rejected flag toggle or tier grant reloaded unchanged with nothing on screen to say it had failed.
     */
    private suspend fun <T> writeThenReload(call: suspend () -> ApiResult<T>) {
        when (val result: ApiResult<T> = call()) {
            is ApiResult.Ok -> fetchSnapshot()
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /** Like [writeThenReload] for a write made from a stay-open dialog: the failure goes back to the dialog, not a toast. */
    private suspend fun <T> reloadAfterDialogWrite(call: suspend () -> ApiResult<T>): ApiResult<T> {
        val result: ApiResult<T> = call()
        if (result is ApiResult.Ok) fetchSnapshot()
        return result
    }

    suspend fun setFeatureFlag(body: AdminSetFeatureFlagRequest) =
        writeThenReload { api.setFeatureFlag(body) }

    /** Hands the outcome back to the confirm dialog, which shows a failure inline and stays open (no toast). */
    suspend fun setFeatureFlagOverride(
        flagKey: String,
        broadcasterId: String,
        body: AdminSetFeatureFlagOverrideRequest,
    ): ApiResult<Unit> = reloadAfterDialogWrite { api.setFeatureFlagOverride(flagKey, broadcasterId, body) }

    suspend fun deleteFeatureFlagOverride(flagKey: String, broadcasterId: String) =
        writeThenReload { api.deleteFeatureFlagOverride(flagKey, broadcasterId) }

    /**
     * Turns what an operator typed into the channel an override targets: a channel id as-is, otherwise an
     * exact (case-insensitive) login match. Operators know channels by login; before this the field took only
     * a raw id, which nobody has to hand. Null, with a toast naming the login, when nothing matches.
     */
    suspend fun resolveOverrideChannel(input: String): ResolvedChannel? {
        val typed: String = input.trim()
        if (typed.isEmpty()) return null
        if (CHANNEL_ID.matches(typed)) {
            val known: String? = _state.value.flagOverrides.firstOrNull { it.broadcasterId == typed }?.channelName?.ifBlank { null }
            return ResolvedChannel(id = typed, displayName = known, login = null)
        }
        val match: AdminChannel? = (api.getChannels(search = typed) as? ApiResult.Ok)
            ?.value
            ?.data
            ?.firstOrNull { it.login.equals(typed, ignoreCase = true) }
        if (match == null) {
            feedback.error(Res.string.admin_flag_override_no_channel, typed)
            return null
        }
        return ResolvedChannel(id = match.id, displayName = match.displayName, login = match.login)
    }

    /**
     * Opens the kill-switch confirm dialog for [flagKey] and fetches its counted blast radius (consequences
     * must be visible before a destructive global-toggle flip commits). Called the moment the operator flips
     * a flag's global switch OFF — [confirmFeatureFlagKillSwitch] / [dismissFeatureFlagKillSwitchPreview]
     * resolve the dialog afterwards.
     */
    suspend fun previewFeatureFlagKillSwitch(flagKey: String) {
        _state.value = _state.value.copy(
            flagKillSwitchKey = flagKey,
            flagKillSwitchPreview = null,
        )
        when (val result = api.previewFeatureFlagBlastRadius(flagKey)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(flagKillSwitchPreview = result.value)
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(flagKillSwitchKey = null)
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    /** Closes the kill-switch confirm dialog without committing anything. */
    fun dismissFeatureFlagKillSwitchPreview() {
        _state.value = _state.value.copy(flagKillSwitchKey = null, flagKillSwitchPreview = null)
    }

    /**
     * Commits the kill switch the confirm dialog previewed. The dialog stays open until this returns; it
     * closes itself on success and shows the failure inline otherwise.
     */
    suspend fun confirmFeatureFlagKillSwitch(body: AdminSetFeatureFlagRequest): ApiResult<FeatureFlag> =
        reloadAfterDialogWrite { api.setFeatureFlag(body) }

    /** Both invite writes come from stay-open dialogs: the dialog closes on success and shows a failure inline. */
    suspend fun createInviteCode(body: AdminCreateInviteCodeRequest): ApiResult<InviteCode> =
        reloadAfterDialogWrite { api.createInviteCode(body) }

    suspend fun revokeInviteCode(inviteCodeId: String): ApiResult<Unit> =
        reloadAfterDialogWrite { api.revokeInviteCode(inviteCodeId) }

    /**
     * Opens the tier edit dialog for [tierId] and fetches its counted blast radius — the real number of
     * tenants on the tier right now — so the owner sees it BEFORE the edit can be saved (consequences must
     * be visible). [confirmTierEdit] echoes the previewed count back on save.
     */
    suspend fun previewTierEdit(tierId: String) {
        _state.value = _state.value.copy(
            tierEditId = tierId,
            tierEditPreview = null,
        )
        when (val result = api.previewTierChange(tierId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(tierEditPreview = result.value)
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(tierEditId = null)
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    /** Closes the tier edit dialog without committing anything. */
    fun dismissTierEditPreview() {
        _state.value = _state.value.copy(tierEditId = null, tierEditPreview = null)
    }

    /** Commits the edit the dialog previewed — [body.confirmedAffectedTenantCount] must match
     * [AdminState.tierEditPreview]'s count or the server rejects it (`PREVIEW_STALE`) rather than
     * applying against a blast radius the owner never actually saw. Closes the dialog either way. */
    suspend fun confirmTierEdit(tierId: String, body: AdminUpdateTierRequest) {
        dismissTierEditPreview()
        writeThenReload { api.updateTier(tierId, body) }
    }

    // ── Comps and entitlement grants (S-ADMIN-4b) ──────────────────────────────

    /** Selects the tenant the grant panel looks at and loads its LIVE grants. */
    suspend fun selectGrantBroadcaster(broadcasterId: String) {
        _state.value = _state.value.copy(
            grantBroadcasterId = broadcasterId,
            entitlementGrants = emptyList(),
            entitlementGrantPreview = null,
        )
        when (val result = api.getEntitlementGrants(broadcasterId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(entitlementGrants = result.value)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /**
     * Fetches the counted blast radius of comping [AdminState.grantBroadcasterId] to [tierId] — the limit
     * keys that would actually change — so the operator sees it BEFORE the grant can be issued (consequences
     * must be visible). [issueEntitlementGrant] echoes the previewed count back on issue.
     */
    suspend fun previewEntitlementGrant(tierId: String) {
        val broadcasterId: String = _state.value.grantBroadcasterId ?: return
        _state.value = _state.value.copy(entitlementGrantPreview = null)
        when (val result = api.previewEntitlementGrant(broadcasterId, tierId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(entitlementGrantPreview = result.value)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /** Clears the staged preview, e.g. when the operator changes the tier they are about to comp to. */
    fun dismissEntitlementGrantPreview() {
        _state.value = _state.value.copy(entitlementGrantPreview = null)
    }

    /** Issues the grant the panel previewed — [body.confirmedChangedLimitCount] must match
     * [AdminState.entitlementGrantPreview]'s count or the server rejects it (`PREVIEW_STALE`) rather than
     * comping against a blast radius the operator never actually saw. Reloads the grant list either way. */
    suspend fun issueEntitlementGrant(body: AdminIssueEntitlementGrantRequest) {
        val broadcasterId: String = _state.value.grantBroadcasterId ?: return
        _state.value = _state.value.copy(entitlementGrantPreview = null)
        when (val result = api.issueEntitlementGrant(broadcasterId, body)) {
            is ApiResult.Ok -> selectGrantBroadcaster(broadcasterId)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /** Authors a brand-new tier. Zero blast radius by construction — no preview/confirm step. */
    suspend fun createTier(body: AdminCreateTierRequest) =
        writeThenReload { api.createTier(body) }

    // ── Priced-unit authoring (S-ADMIN-4d) ─────────────────────────────────────

    /** Opens the author-a-price dialog. [editingUnitKey] set = reprice that existing row; null = a
     * brand-new unit key. */
    fun openPricedUnitForm(editingUnitKey: String?) {
        _state.value = _state.value.copy(
            pricedUnitFormOpen = true,
            pricedUnitFormEditingKey = editingUnitKey,
        )
    }

    /** Closes the author-a-price dialog without committing anything. */
    fun dismissPricedUnitForm() {
        _state.value = _state.value.copy(pricedUnitFormOpen = false, pricedUnitFormEditingKey = null)
    }

    /** Authors (creates or reprices) one usage unit's price. Upsert by unit key — ships EMPTY, so a unit
     * with no row stays unpriced until the owner does this. Closes the dialog on success. */
    suspend fun authorPricedUnit(body: AdminAuthorPricedUnitRequest) {
        dismissPricedUnitForm()
        writeThenReload { api.authorPricedUnit(body) }
    }

    suspend fun grantTier(broadcasterId: String, body: AdminGrantTierRequest) =
        writeThenReload { api.grantTier(broadcasterId, body) }

    suspend fun grantFounderBadge(broadcasterId: String) =
        writeThenReload { api.grantFounderBadge(broadcasterId) }

    // ── Invoices, dunning and refunds (S-ADMIN-4c) ─────────────────────────────

    /** Selects the tenant the invoices panel looks at and loads its invoices. */
    suspend fun selectInvoiceBroadcaster(broadcasterId: String) {
        _state.value = _state.value.copy(
            invoiceBroadcasterId = broadcasterId,
            invoices = emptyList(),
            refundPendingInvoiceId = null,
        )
        when (val result = api.getInvoices(broadcasterId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(invoices = result.value)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /** Opens the refund confirmation for [invoiceId] — the dialog reads the amount/currency straight off the
     * already-loaded row in [AdminState.invoices], so the operator sees exactly what will be refunded before
     * confirming (consequences must be visible). */
    fun stageRefund(invoiceId: String) {
        _state.value = _state.value.copy(refundPendingInvoiceId = invoiceId)
    }

    /** Closes the refund confirmation without refunding anything. */
    fun dismissRefund() {
        _state.value = _state.value.copy(refundPendingInvoiceId = null)
    }

    /** Refunds [invoiceId] — the server rejects (`VALIDATION_FAILED`) anything but a `Paid` invoice, so an
     * already-refunded row can never be double-refunded even if the dialog is somehow reopened on it. Reloads
     * the invoice list either way so the new `refunded` status and amount are visible immediately. */
    suspend fun confirmRefund(invoiceId: String) {
        val broadcasterId: String = _state.value.invoiceBroadcasterId ?: return
        _state.value = _state.value.copy(refundPendingInvoiceId = null)
        when (val result = api.refundInvoice(invoiceId)) {
            is ApiResult.Ok -> selectInvoiceBroadcaster(broadcasterId)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    // ── IAM ───────────────────────────────────────────────────────────────────

    suspend fun loadIam() {
        _state.value = _state.value.copy(iamLoading = true, iamError = null)
        val rolesResult = iamApi.listRoles()
        val principalsResult = iamApi.listPrincipals()
        _state.value = _state.value.copy(
            roles = (rolesResult as? ApiResult.Ok)?.value ?: emptyList(),
            principals = (principalsResult as? ApiResult.Ok)?.value ?: emptyList(),
            iamLoading = false,
            iamError = listOf(rolesResult, principalsResult)
                .filterIsInstance<ApiResult.Failure>()
                .firstOrNull()
                ?.error
                ?.message,
        )
    }

    suspend fun loadEffectivePermissions(principalId: String) {
        when (val result: ApiResult<List<String>> = iamApi.effectivePermissions(principalId)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    effectivePermissions = _state.value.effectivePermissions + (principalId to result.value),
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(iamError = result.error.message)
        }
    }

    /** Promote a user (employee) — [userId] set, [principalType] 0. The panel shows no key. */
    suspend fun promoteUser(userId: String, displayName: String, roleIds: List<String>): ApiResult<IamPrincipal> {
        val body = CreatePrincipalBody(principalType = 0, userId = userId, displayName = displayName, roleIds = roleIds)
        val result: ApiResult<IamPrincipal> = iamApi.createPrincipal(body)
        if (result is ApiResult.Ok) loadIam()
        return result
    }

    /**
     * Create a service account — [principalType] 1. Its key is returned ONCE; stash it for the show-once dialog.
     * The caller's dialog shows a failure inline, so no toast is raised here.
     */
    suspend fun createServiceAccount(serviceAccountName: String, roleIds: List<String>): ApiResult<IamPrincipal> {
        val body = CreatePrincipalBody(principalType = 1, displayName = serviceAccountName, roleIds = roleIds, serviceAccountName = serviceAccountName)
        val result: ApiResult<IamPrincipal> = iamApi.createPrincipal(body)
        if (result is ApiResult.Ok) {
            _state.value = _state.value.copy(issuedServiceAccountKey = result.value.serviceAccountKey)
            loadIam()
        }
        return result
    }

    /** The failure toast for a write whose caller has no dialog to show the reason in. */
    fun reportActionFailure(error: ApiError) {
        feedback.error(Res.string.admin_action_error, error.message)
    }

    /** Clears the show-once service-account key once the operator has copied/dismissed it. */
    fun dismissIssuedKey() {
        _state.value = _state.value.copy(issuedServiceAccountKey = null)
    }

    /** The caller's dialog shows a failure inline (self-deactivation returns VALIDATION_FAILED), so no toast. */
    suspend fun deactivatePrincipal(principalId: String, reason: String?): ApiResult<Unit> {
        val result: ApiResult<Unit> = iamApi.deactivatePrincipal(principalId, reason)
        if (result is ApiResult.Ok) loadIam()
        return result
    }

    suspend fun reactivatePrincipal(principalId: String) {
        when (val result: ApiResult<Unit> = iamApi.reactivatePrincipal(principalId)) {
            is ApiResult.Ok -> loadIam()
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    suspend fun assignRole(principalId: String, roleId: String, reason: String?): ApiResult<IamRoleAssignment> {
        val body = AssignRoleBody(principalId = principalId, roleId = roleId, reason = reason)
        val result: ApiResult<IamRoleAssignment> = iamApi.assignRole(body)
        if (result is ApiResult.Ok) loadIam()
        return result
    }

    /** The caller's dialog shows a failure inline, so no toast is raised here. */
    suspend fun revokeAssignment(assignmentId: String, reason: String?): ApiResult<Unit> {
        val result: ApiResult<Unit> = iamApi.revokeAssignment(assignmentId, reason)
        if (result is ApiResult.Ok) loadIam()
        return result
    }

    // ── Provider app credentials ────────────────────────────────────────────

    /** Reads every provider's credential state. Fatal on failure: an empty list would read as "nothing
     * configured", which for a credentials screen is the most misleading thing it could say. */
    suspend fun loadProviders() {
        _state.value = _state.value.copy(providersLoading = true, providersError = null)
        when (val result: ApiResult<List<ProviderCredential>> = api.getProviderCredentials()) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    providerCredentials = result.value,
                    providersLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    providersError = result.error.message,
                    providersLoading = false,
                )
        }
    }

    /** Stores a client id, secret and/or (YouTube only) Data API key. Blank fields are dropped, so the server is never asked to
     * overwrite a value the operator left alone. The result goes back to the open dialog, which shows a failure inline. */
    suspend fun saveProviderCredential(
        provider: String,
        clientId: String,
        clientSecret: String,
        apiKey: String,
    ): ApiResult<ProviderCredential> {
        val body = SaveProviderCredentialBody(
            clientId = clientId.takeIf { it.isNotBlank() },
            clientSecret = clientSecret.takeIf { it.isNotBlank() },
            apiKey = apiKey.takeIf { it.isNotBlank() },
        )
        val result: ApiResult<ProviderCredential> = api.saveProviderCredential(provider, body)
        if (result is ApiResult.Ok) loadProviders()
        return result
    }

    /** Clears the stored rows so the environment resolves again. The result goes back to the open dialog. */
    suspend fun clearProviderCredential(provider: String): ApiResult<ProviderCredential> {
        val result: ApiResult<ProviderCredential> = api.clearProviderCredential(provider)
        if (result is ApiResult.Ok) loadProviders()
        return result
    }

    // ── Tenants ─────────────────────────────────────────────────────────────

    /**
     * Reloads the tenant list under exactly [search] and [status] (null = every status), at [page] — a filter
     * change passes none and so returns to page 1. Both are passed
     * explicitly: the "All" chip clears the status by passing null, and a null must be honoured, never read
     * as "keep the previous filter".
     */
    suspend fun loadTenants(search: String, status: String?, page: Int = 1) {
        _state.value = _state.value.copy(
            tenantsLoading = true,
            tenantsError = null,
            tenantSearch = search,
            tenantStatusFilter = status,
            tenantsPage = page,
        )
        when (val result = platformAdminApi.listTenants(search = search, status = status, page = page)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    tenants = result.value.data,
                    tenantsHasMore = result.value.hasMore,
                    tenantsLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(tenantsLoading = false, tenantsError = result.error.message)
        }
    }

    suspend fun openTenant(broadcasterId: String) {
        when (val result: ApiResult<AdminTenantDetail> = platformAdminApi.getTenant(broadcasterId)) {
            is ApiResult.Ok -> _state.value = _state.value.copy(selectedTenant = result.value)
            is ApiResult.Failure -> _state.value = _state.value.copy(tenantsError = result.error.message)
        }
    }

    fun closeTenant() {
        _state.value = _state.value.copy(selectedTenant = null)
    }

    suspend fun suspendTenant(broadcasterId: String, newStatus: String, reason: String) {
        when (val result: ApiResult<Unit> = platformAdminApi.suspendTenant(broadcasterId, SuspendTenantBody(newStatus, reason))) {
            is ApiResult.Ok -> {
                loadTenants(
                    search = _state.value.tenantSearch,
                    status = _state.value.tenantStatusFilter,
                    page = _state.value.tenantsPage,
                )
                if (_state.value.selectedTenant?.id == broadcasterId) openTenant(broadcasterId)
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    suspend fun reinstateTenant(broadcasterId: String, justification: String) {
        when (val result: ApiResult<Unit> = platformAdminApi.reinstateTenant(broadcasterId, ReinstateTenantBody(justification))) {
            is ApiResult.Ok -> {
                loadTenants(
                    search = _state.value.tenantSearch,
                    status = _state.value.tenantStatusFilter,
                    page = _state.value.tenantsPage,
                )
                if (_state.value.selectedTenant?.id == broadcasterId) openTenant(broadcasterId)
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    suspend fun beginTenantAccess(broadcasterId: String, justification: String, breakGlass: Boolean) {
        val body = BeginTenantAccessBody(justification = justification, breakGlass = breakGlass)
        when (val result = platformAdminApi.beginAccess(broadcasterId, body)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    fun setTenantContentJustification(value: String) {
        _state.value = _state.value.copy(tenantContentJustification = value)
    }

    /**
     * The custom (non-platform) commands and (non-platform-sourced) pipelines this ONE tenant built for
     * themselves — read-only investigation surface (support/moderation/abuse triage), gated on the same
     * `user:support:view` key as the rest of the support desk. Never edits the tenant's own content: only
     * the platform content-authoring plane (Content tab) writes anything.
     *
     * The sheet opens first and the read waits for a typed justification: the justification field lives
     * inside the sheet, so refusing to open on a blank one would make the sheet unreachable.
     */
    suspend fun openTenantContent(broadcasterId: String) {
        val api: AdminSupportApi = supportApi ?: return
        _state.value = _state.value.copy(tenantContentOpenFor = broadcasterId)
        val justification: String = _state.value.tenantContentJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(
            tenantContentLoading = true,
            tenantContentError = null,
            tenantCommands = emptyList(),
            tenantPipelines = emptyList(),
        )
        val commandsResult = api.getTenantCommands(channelId = broadcasterId, justification = justification)
        val pipelinesResult = api.getTenantPipelines(channelId = broadcasterId, justification = justification)

        val commandsError: String? = (commandsResult as? ApiResult.Failure)?.error?.message
        val pipelinesError: String? = (pipelinesResult as? ApiResult.Failure)?.error?.message
        _state.value = _state.value.copy(
            tenantCommands = (commandsResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            tenantPipelines = (pipelinesResult as? ApiResult.Ok)?.value?.data ?: emptyList(),
            tenantContentLoading = false,
            tenantContentError = commandsError ?: pipelinesError,
        )
    }

    fun closeTenantContent() {
        _state.value = _state.value.copy(
            tenantContentOpenFor = null,
            tenantContentError = null,
            tenantCommands = emptyList(),
            tenantPipelines = emptyList(),
        )
    }

    // ── Audit ─────────────────────────────────────────────────────────────────

    /**
     * Reloads the audit log under exactly [outcome] (null = every outcome) and [permission] (blank = any).
     * Both are passed explicitly so the "All outcomes" chip can clear the filter with a null that is honoured.
     */
    suspend fun loadAudit(outcome: String?, permission: String, page: Int = 1) {
        _state.value = _state.value.copy(
            auditLoading = true,
            auditError = null,
            auditOutcomeFilter = outcome,
            auditPermissionFilter = permission,
            auditPage = page,
        )
        when (val result = platformAdminApi.searchAudit(permission = permission, outcome = outcome, page = page)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    auditEntries = result.value.data,
                    auditHasMore = result.value.hasMore,
                    auditLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(auditLoading = false, auditError = result.error.message)
        }
    }

    // ── Cross-tenant support desk (S-ADMIN-7a) ───────────────────────

    /** True when this build wired a support-desk client — the tab is hidden rather than dead without one. */
    val supportDeskAvailable: Boolean get() = supportApi != null

    fun setSupportSearch(value: String) {
        _state.value = _state.value.copy(supportSearch = value)
    }

    fun setSupportJustification(value: String) {
        _state.value = _state.value.copy(supportJustification = value)
    }

    /** Closes the open person record, returning to the result list. */
    fun closeSupportPerson() {
        _state.value = _state.value.copy(
            supportPerson = null,
            supportPersonError = null,
            supportHistory = emptyList(),
            supportHistoryLoaded = false,
            supportHistoryError = null,
        )
    }

    /**
     * Finds people across EVERY tenant. The backend refuses a blank justification and audits the call, so the
     * caller is expected to have both fields filled — this only guards against firing a call it knows is
     * invalid.
     */
    suspend fun searchPeople() {
        val api: AdminSupportApi = supportApi ?: return
        val search: String = _state.value.supportSearch.trim()
        val justification: String = _state.value.supportJustification.trim()
        if (search.isBlank() || justification.isBlank()) return

        _state.value = _state.value.copy(supportLoading = true, supportError = null, supportPerson = null)
        when (val result = api.searchPeople(search = search, justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    supportResults = result.value.data,
                    supportSearched = true,
                    supportLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(supportLoading = false, supportError = result.error.message)
        }
    }

    /**
     * Opens ONE person's cross-tenant record, then replays what actually happened to them. The same
     * justification rides both audit rows as the search. History loads AFTER the record so an operator who
     * only needed the current state never pays for the replay call when the record itself failed.
     */
    suspend fun openSupportPerson(subjectUserId: String) {
        val api: AdminSupportApi = supportApi ?: return
        val justification: String = _state.value.supportJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(supportPersonLoading = true, supportPersonError = null)
        when (val result = api.getPerson(subjectUserId = subjectUserId, justification = justification)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(supportPerson = result.value, supportPersonLoading = false)
                loadSupportHistory(subjectUserId = subjectUserId, justification = justification)
            }
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    supportPersonLoading = false,
                    supportPersonError = result.error.message,
                )
        }
    }

    /** Replays what actually happened to this person, across every tenant, from the real event journal. */
    private suspend fun loadSupportHistory(subjectUserId: String, justification: String) {
        val api: AdminSupportApi = supportApi ?: return

        _state.value = _state.value.copy(supportHistoryLoading = true, supportHistoryError = null)
        when (
            val result = api.getPersonHistory(subjectUserId = subjectUserId, justification = justification)
        ) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    supportHistory = result.value.data,
                    supportHistoryLoaded = true,
                    supportHistoryLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    supportHistoryLoading = false,
                    supportHistoryError = result.error.message,
                )
        }
    }

    // ── Platform-wide trust & safety desk (S-ADMIN-8a) ───────────────────────

    /** True when this build wired a trust-safety client — the tab is hidden rather than dead without one. */
    val trustSafetyAvailable: Boolean get() = trustSafetyApi != null

    // ── GDPR data requests, platform-wide (A7) ───────────────────────

    /** True when this build wired a compliance client — the tab is hidden rather than dead without one. */
    val complianceAvailable: Boolean get() = complianceApi != null

    /**
     * Reloads the platform-wide request list under exactly [status] / [requestType] (null = all) and the
     * status summary beside it, so the counts and the rows always describe the same ledger moment. Both
     * filters are passed explicitly: a chip that clears a filter passes null and the null is honoured.
     */
    suspend fun loadDataRequests(status: String?, requestType: String?) {
        val api: ComplianceApi = complianceApi ?: return
        _state.value = _state.value.copy(
            dataRequestsLoading = true,
            dataRequestsError = null,
            dataRequestStatusFilter = status,
            dataRequestTypeFilter = requestType,
        )
        val summary: ApiResult<ErasureRequestSummary> = api.summary()
        if (summary is ApiResult.Failure) {
            _state.value = _state.value.copy(dataRequestsLoading = false, dataRequestsError = summary.error.message)
            return
        }
        val page: ApiResult<PaginatedEnvelope<ErasureRequest>> = api.listRequests(status = status, requestType = requestType)
        when (page) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    dataRequests = page.value.data,
                    dataRequestSummary = (summary as ApiResult.Ok<ErasureRequestSummary>).value,
                    dataRequestsLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(dataRequestsLoading = false, dataRequestsError = page.error.message)
        }
    }

    /**
     * Opens the "Re-run erasure" confirm for a failed [request] and reads the subject's counted blast radius
     * for it. The confirm stays withheld until that count arrives; a failed lookup is recorded as failed,
     * never as "nothing would be erased".
     */
    suspend fun stageErasureRetry(request: ErasureRequest) {
        val api: ComplianceApi = complianceApi ?: return
        _state.value = _state.value.copy(
            erasureRetryTarget = request,
            erasureRetryPreview = null,
            erasureRetryPreviewFailed = false,
            erasureRetryRunning = false,
            erasureRetryError = null,
        )
        val preview: ApiResult<ErasurePreview> = api.previewErasure(request.subjectUserId)
        // The operator may have closed or switched the dialog while the count loaded.
        if (_state.value.erasureRetryTarget?.id != request.id) return
        _state.value = when (preview) {
            is ApiResult.Ok -> _state.value.copy(erasureRetryPreview = preview.value)
            is ApiResult.Failure -> _state.value.copy(erasureRetryPreviewFailed = true)
        }
    }

    /** Closes the re-run confirm without erasing anything. */
    fun dismissErasureRetry() {
        _state.value = _state.value.copy(
            erasureRetryTarget = null,
            erasureRetryPreview = null,
            erasureRetryPreviewFailed = false,
            erasureRetryRunning = false,
            erasureRetryError = null,
        )
    }

    /**
     * Re-runs the staged failed erasure. Success replaces the row with the server's completed request, closes
     * the dialog and refreshes the status counts. A refusal or a second failure keeps the dialog open with
     * the reason, so the operator sees why nothing changed.
     */
    suspend fun confirmErasureRetry() {
        val api: ComplianceApi = complianceApi ?: return
        val target: ErasureRequest = _state.value.erasureRetryTarget ?: return
        if (_state.value.erasureRetryRunning) return
        _state.value = _state.value.copy(erasureRetryRunning = true, erasureRetryError = null)
        when (val result: ApiResult<ErasureRequest> = api.retryErasure(target.id)) {
            is ApiResult.Ok -> {
                val updated: ErasureRequest = result.value
                _state.value = _state.value.copy(
                    dataRequests = _state.value.dataRequests.map { if (it.id == updated.id) updated else it },
                )
                dismissErasureRetry()
                val summary: ApiResult<ErasureRequestSummary> = api.summary()
                if (summary is ApiResult.Ok) _state.value = _state.value.copy(dataRequestSummary = summary.value)
            }
            is ApiResult.Failure ->
                _state.value = _state.value.copy(erasureRetryRunning = false, erasureRetryError = result.error)
        }
    }

    fun setTrustSafetyJustification(value: String) {
        _state.value = _state.value.copy(trustSafetyJustification = value)
    }

    /** Every actor whose spam-defence detections were recorded in two or more tenants, with the real
     * detections that back the correlation. The backend refuses a blank justification and audits the call. */
    suspend fun loadCrossTenantSignals() {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(crossTenantSignalsLoading = true, crossTenantSignalsError = null)
        when (val result = api.getCrossTenantSignals(justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    crossTenantSignals = result.value,
                    crossTenantSignalsLoaded = true,
                    crossTenantSignalsLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    crossTenantSignalsLoading = false,
                    crossTenantSignalsError = result.error.message,
                )
        }
    }

    /** The queue of automatic account actions awaiting review, each carrying the evidence that caused it. */
    suspend fun loadReviewQueue() {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(reviewQueueLoading = true, reviewQueueError = null)
        when (val result = api.getReviewQueue(justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    reviewQueue = result.value.data,
                    reviewQueueLoaded = true,
                    reviewQueueLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    reviewQueueLoading = false,
                    reviewQueueError = result.error.message,
                )
        }
    }

    /** Opens the destructive confirm dialog for [item] — its [TrustSafetyReviewItem.reversalPreview] is
     * shown as the dialog's message, so the operator sees the blast radius before committing to it. */
    fun requestOverturn(item: TrustSafetyReviewItem) {
        _state.value = _state.value.copy(reviewItemPendingOverturn = item)
    }

    fun dismissOverturnRequest() {
        _state.value = _state.value.copy(reviewItemPendingOverturn = null)
    }

    /** The operator agrees with an automatic action: closes the review, the action itself is untouched. */
    suspend fun confirmReviewItem(detectionId: String) {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(reviewActionInFlight = detectionId)
        when (val result = api.confirm(detectionId = detectionId, justification = justification)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(reviewActionInFlight = null)
                feedback.success(Res.string.admin_review_confirmed)
                loadReviewQueue()
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(reviewActionInFlight = null)
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    /** The operator disagrees: the backend REVERSES the real account action before marking the detection
     * overturned — a failed reversal leaves the queue row exactly as it was. The outcome goes back to the
     * stay-open confirm: a failure shows inline there (no toast) and the pending overturn is kept. */
    suspend fun overturnReviewItem(detectionId: String): ApiResult<Unit> {
        val api: TrustSafetyApi = trustSafetyApi ?: return notSent()
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return notSent()

        _state.value = _state.value.copy(reviewActionInFlight = detectionId)
        val result: ApiResult<Unit> = api.overturn(detectionId = detectionId, justification = justification)
        _state.value = _state.value.copy(reviewActionInFlight = null)
        if (result is ApiResult.Ok) {
            _state.value = _state.value.copy(reviewItemPendingOverturn = null)
            feedback.success(Res.string.admin_review_overturned)
            loadReviewQueue()
        }
        return result
    }

    /** A guarded no-op (no API, no justification, already running): a blank reason shows the dialog's generic line. */
    private fun notSent(): ApiResult.Failure = ApiResult.Failure(ApiError(0, "NOT_SENT", ""))

    // ── Network-wide block (S-ADMIN-8b) ──────────────────────────────────────
    // The most dangerous control in the product: it acts across EVERY tenant of this deployment at
    // once. A preview showing the REAL counted blast radius is mandatory before apply is even offered,
    // and the confirmed count travelling with the apply call is what the server fails closed against.

    fun setNetworkBlockTargetTwitchUserId(value: String) {
        _state.value = _state.value.copy(
            networkBlockTargetTwitchUserId = value,
            // A changed target invalidates any preview already on screen — never let a stale blast
            // radius linger next to a different target.
            networkBlockPreview = null,
            networkBlockPreviewError = null,
        )
    }

    fun setNetworkBlockReason(value: String) {
        _state.value = _state.value.copy(networkBlockReason = value)
    }

    /** The real, freshly-computed blast radius a block against this target would touch. */
    suspend fun previewNetworkBlock() {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val target: String = _state.value.networkBlockTargetTwitchUserId.trim()
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (target.isBlank() || justification.isBlank()) return

        _state.value = _state.value.copy(
            networkBlockPreviewLoading = true,
            networkBlockPreviewError = null,
            networkBlockPreview = null,
        )
        when (val result = api.previewNetworkBlock(targetTwitchUserId = target, justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    networkBlockPreview = result.value,
                    networkBlockPreviewLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    networkBlockPreviewLoading = false,
                    networkBlockPreviewError = result.error.message,
                )
        }
    }

    /** Opens the destructive confirm dialog — only reachable once a real preview is on screen. */
    fun requestApplyNetworkBlock() {
        if (_state.value.networkBlockPreview == null) return
        _state.value = _state.value.copy(networkBlockApplyConfirmOpen = true)
    }

    fun dismissApplyNetworkBlockRequest() {
        _state.value = _state.value.copy(networkBlockApplyConfirmOpen = false)
    }

    /** Applies the block, echoing back the exact count the operator was just shown — a stale count (the
     * blast radius moved since the preview) is refused server-side rather than silently acted on. */
    suspend fun applyNetworkBlock(): ApiResult<Unit> {
        val api: TrustSafetyApi = trustSafetyApi ?: return notSent()
        val preview: NetworkBlockPreview = _state.value.networkBlockPreview ?: return notSent()
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return notSent()
        // A second confirm while the first is in flight would ban every tenant a second time.
        if (_state.value.networkBlockApplyInFlight) return notSent()

        _state.value = _state.value.copy(networkBlockApplyInFlight = true)
        val result: ApiResult<NetworkBlock> = api.applyNetworkBlock(
            targetTwitchUserId = preview.targetTwitchUserId,
            reason = _state.value.networkBlockReason.trim().ifBlank { null },
            justification = justification,
            confirmedTenantCount = preview.tenantCount,
        )
        when (result) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(
                    networkBlockApplyInFlight = false,
                    networkBlockApplyConfirmOpen = false,
                    networkBlockPreview = null,
                    networkBlockTargetTwitchUserId = "",
                    networkBlockReason = "",
                )
                feedback.success(Res.string.admin_network_block_applied, result.value.channelCount)
                loadNetworkBlocks()
                return ApiResult.Ok(Unit)
            }
            is ApiResult.Failure -> {
                // The confirm stays open and shows the reason inline; the preview and typed reason are kept.
                _state.value = _state.value.copy(networkBlockApplyInFlight = false)
                return result
            }
        }
    }

    /** Every network block, newest first — active/partial ones are still enforced. */
    suspend fun loadNetworkBlocks() {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(networkBlocksLoading = true, networkBlocksError = null)
        when (val result = api.listNetworkBlocks(justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    networkBlocks = result.value,
                    networkBlocksLoaded = true,
                    networkBlocksLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    networkBlocksLoading = false,
                    networkBlocksError = result.error.message,
                )
        }
    }

    /** Lifts a network block — the backend stamps it fully lifted only when every tenant leg actually
     * restores; a partial outcome comes back honestly and this reloads the list to show it. */
    suspend fun liftNetworkBlock(blockId: String) {
        val api: TrustSafetyApi = trustSafetyApi ?: return
        val justification: String = _state.value.trustSafetyJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(networkBlockLiftInFlight = blockId)
        when (val result = api.liftNetworkBlock(blockId = blockId, justification = justification)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(networkBlockLiftInFlight = null)
                loadNetworkBlocks()
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(networkBlockLiftInFlight = null)
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    // ── Shared platform bot (S-BOT-PLATFORM-UI) ──────────────────────────────
    // Once first-run setup is done, this is the ONLY surface left to re-connect or replace the shared
    // platform bot — the channel Integrations screen deliberately polls the channel-scoped endpoint
    // instead. A swap is platform-wide, so a real, freshly-counted blast radius is mandatory before the
    // destructive reconnect is even offered, exactly like the network-wide block above.

    /** True when this build wired a platform-bot-admin client — the tab is hidden rather than dead without one. */
    val platformBotAdminAvailable: Boolean get() = platformBotAdminApi != null

    fun setPlatformBotJustification(value: String) {
        _state.value = _state.value.copy(
            platformBotJustification = value,
            // A changed justification invalidates any preview already on screen.
            platformBotReconnectPreview = null,
            platformBotReconnectPreviewError = null,
        )
    }

    /** The real current state — never-connected / working / token-unusable (an ENCRYPTION_KEY rotation). */
    suspend fun loadPlatformBotStatus() {
        val api: PlatformBotAdminApi = platformBotAdminApi ?: return
        _state.value = _state.value.copy(platformBotStatusLoading = true, platformBotStatusError = null)
        when (val result = api.status()) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    platformBotStatus = result.value,
                    platformBotStatusLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    platformBotStatusLoading = false,
                    platformBotStatusError = result.error.message,
                )
        }
    }

    /** The real, freshly-computed blast radius a reconnect would touch. */
    suspend fun previewPlatformBotReconnect() {
        val api: PlatformBotAdminApi = platformBotAdminApi ?: return
        val justification: String = _state.value.platformBotJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(
            platformBotReconnectPreviewLoading = true,
            platformBotReconnectPreviewError = null,
            platformBotReconnectPreview = null,
        )
        when (val result = api.previewReconnect(justification = justification)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    platformBotReconnectPreview = result.value,
                    platformBotReconnectPreviewLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    platformBotReconnectPreviewLoading = false,
                    platformBotReconnectPreviewError = result.error.message,
                )
        }
    }

    /** Opens the destructive confirm dialog — only reachable once a real preview is on screen. */
    fun requestPlatformBotReconnect() {
        if (_state.value.platformBotReconnectPreview == null) return
        _state.value = _state.value.copy(platformBotReconnectConfirmOpen = true)
    }

    fun dismissPlatformBotReconnectRequest() {
        _state.value = _state.value.copy(platformBotReconnectConfirmOpen = false)
    }

    /** Abandon an in-flight reconnect device login (the operator closed the panel) without waiting further. */
    fun cancelPlatformBotReconnect() {
        _state.value = _state.value.copy(platformBotReconnectDevice = null)
    }

    /** Begins the reconnect device login, echoing back the exact count the operator was just shown — a
     * stale count (the blast radius moved since the preview) is refused server-side rather than acted on. */
    suspend fun confirmPlatformBotReconnect() {
        val api: PlatformBotAdminApi = platformBotAdminApi ?: return
        val preview: PlatformBotReconnectPreview = _state.value.platformBotReconnectPreview ?: return
        val justification: String = _state.value.platformBotJustification.trim()
        if (justification.isBlank()) return

        _state.value = _state.value.copy(platformBotReconnectConfirmOpen = false)
        when (
            val start = api.startReconnect(
                justification = justification,
                confirmedAffectedChannelCount = preview.affectedChannelCount,
            )
        ) {
            is ApiResult.Failure ->
                if (start.error.code == PREVIEW_STALE) {
                    failPlatformBotReconnectAsStale(start.error.message)
                } else {
                    feedback.error(Res.string.admin_action_error, start.error.message)
                }
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(
                    platformBotReconnectDevice = PlatformBotReconnectDeviceState(
                        userCode = start.value.userCode,
                        verificationUri = start.value.verificationUri,
                    ),
                )
                pollPlatformBotReconnect(start.value, justification, preview.affectedChannelCount)
            }
        }
    }

    // Poll the reconnect device endpoint on its own interval until the operator approves (→ reload the
    // real status, never an optimistic flip), declines, or the code expires. A transient poll failure is
    // tolerated until the deadline so a blip mid-approval doesn't abort the reconnect. The loop bails
    // immediately if [cancelPlatformBotReconnect] cleared the device panel out from under it.
    private suspend fun pollPlatformBotReconnect(
        start: DeviceCodeStart,
        justification: String,
        confirmedAffectedChannelCount: Int,
    ) {
        val api: PlatformBotAdminApi = platformBotAdminApi ?: return
        val intervalMs: Long = start.interval.coerceAtLeast(1).toLong() * 1000L
        val deadlineMs: Long = start.expiresIn.coerceAtLeast(1).toLong() * 1000L
        var elapsedMs: Long = 0

        while (elapsedMs < deadlineMs && _state.value.platformBotReconnectDevice != null) {
            delay(intervalMs)
            elapsedMs += intervalMs

            when (
                val poll: ApiResult<DeviceBotPoll> = api.pollReconnect(
                    deviceCode = start.deviceCode,
                    justification = justification,
                    confirmedAffectedChannelCount = confirmedAffectedChannelCount,
                )
            ) {
                is ApiResult.Failure -> {
                    // A moved blast radius is final, not transient: polling on would spin until the code
                    // expired. Drop the device panel and the now-wrong preview so the operator re-previews.
                    if (poll.error.code == PREVIEW_STALE) {
                        failPlatformBotReconnectAsStale(poll.error.message)
                        return
                    }
                    // Anything else is tolerated until the code's deadline.
                }
                is ApiResult.Ok ->
                    when (poll.value.status) {
                        "authorized" -> {
                            // The credential is replaced server-side; re-read the authoritative status.
                            _state.value = _state.value.copy(
                                platformBotReconnectDevice = null,
                                platformBotReconnectPreview = null,
                                platformBotJustification = "",
                            )
                            loadPlatformBotStatus()
                            return
                        }
                        "expired", "denied", "error" -> {
                            _state.value = _state.value.copy(platformBotReconnectDevice = null)
                            feedback.error(Res.string.admin_action_error, poll.value.status)
                            return
                        }
                        else -> Unit // pending / slow_down — keep polling.
                    }
            }
        }
        if (_state.value.platformBotReconnectDevice != null) {
            _state.value = _state.value.copy(platformBotReconnectDevice = null)
            feedback.error(Res.string.admin_action_error, "expired")
        }
    }

    private fun failPlatformBotReconnectAsStale(message: String) {
        _state.value = _state.value.copy(
            platformBotReconnectDevice = null,
            platformBotReconnectPreview = null,
        )
        feedback.error(Res.string.admin_action_error, message)
    }

    // ── EventSub subscription health (S-ADMIN-6a) ───────────────────────────────

    /** Loads the REAL EventSub registry, grouped by tenant — never a fabricated list. [page] defaults to
     * the page already showing, so a reload (e.g. after a write) stays where the operator was. */
    suspend fun loadEventSubHealth(page: Int? = null) {
        val effectivePage: Int = page ?: _state.value.eventSubHealthPage
        _state.value = _state.value.copy(
            eventSubHealthLoading = true,
            eventSubHealthError = null,
            eventSubHealthPage = effectivePage,
        )
        when (val result = api.getEventSubHealth(page = effectivePage)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    eventSubHealth = result.value.data,
                    eventSubHealthHasMore = result.value.hasMore,
                    eventSubHealthLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(eventSubHealthLoading = false, eventSubHealthError = result.error.message)
        }
    }

    // ── Outbound webhook delivery log + replay (S-ADMIN-6a) ─────────────────────

    suspend fun loadWebhookDeliveries(page: Int? = null) {
        val effectivePage: Int = page ?: _state.value.webhookDeliveriesPage
        _state.value = _state.value.copy(
            webhookDeliveriesLoading = true,
            webhookDeliveriesError = null,
            webhookDeliveriesPage = effectivePage,
        )
        when (val result = api.getWebhookDeliveries(page = effectivePage)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    webhookDeliveries = result.value.data,
                    webhookDeliveriesHasMore = result.value.hasMore,
                    webhookDeliveriesLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(webhookDeliveriesLoading = false, webhookDeliveriesError = result.error.message)
        }
    }

    /** Opens the replay confirmation for [deliveryId] — nothing is sent until [confirmWebhookReplay]. */
    fun stageWebhookReplay(deliveryId: Long) {
        _state.value = _state.value.copy(replayPendingDeliveryId = deliveryId)
    }

    fun dismissWebhookReplay() {
        _state.value = _state.value.copy(replayPendingDeliveryId = null)
    }

    /** Commits the replay: sends a genuinely NEW delivery attempt (the original stays exactly as it was),
     * then reloads the log so the new row is visible immediately. */
    suspend fun confirmWebhookReplay() {
        val deliveryId: Long = _state.value.replayPendingDeliveryId ?: return
        when (val result = api.replayWebhookDelivery(deliveryId)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(replayPendingDeliveryId = null)
                loadWebhookDeliveries()
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    // ── Background job queue + retry (S-ADMIN-6b) ───────────────────────────────

    /** Loads the REAL background job queue — every `ScheduledPipelineTask` row, never a fabricated list. */
    suspend fun loadScheduledJobs(page: Int? = null) {
        val effectivePage: Int = page ?: _state.value.scheduledJobsPage
        _state.value = _state.value.copy(
            scheduledJobsLoading = true,
            scheduledJobsError = null,
            scheduledJobsPage = effectivePage,
        )
        when (val result = api.getScheduledJobs(page = effectivePage)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    scheduledJobs = result.value.data,
                    scheduledJobsHasMore = result.value.hasMore,
                    scheduledJobsLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(scheduledJobsLoading = false, scheduledJobsError = result.error.message)
        }
    }

    /** Opens the retry confirmation for [taskId] — nothing is sent until [confirmScheduledJobRetry]. */
    fun stageScheduledJobRetry(taskId: String) {
        _state.value = _state.value.copy(retryPendingJobId = taskId)
    }

    fun dismissScheduledJobRetry() {
        _state.value = _state.value.copy(retryPendingJobId = null)
    }

    /** Commits the retry: schedules a brand-new deferred run for the same pipeline (the original failed
     * attempt stays exactly as it was), then reloads the queue so the new row is visible immediately. */
    suspend fun confirmScheduledJobRetry() {
        val taskId: String = _state.value.retryPendingJobId ?: return
        when (val result = api.retryScheduledJob(taskId)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(retryPendingJobId = null)
                feedback.success(Res.string.admin_job_retry_scheduled)
                loadScheduledJobs()
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    // ── Per-tenant usage (S-ADMIN-6b) ───────────────────────────────────────────

    /** Loads per-tenant usage for each tenant's most recent metering period, computed from recorded usage. */
    suspend fun loadTenantUsage(page: Int? = null) {
        val effectivePage: Int = page ?: _state.value.tenantUsagePage
        _state.value = _state.value.copy(
            tenantUsageLoading = true,
            tenantUsageError = null,
            tenantUsagePage = effectivePage,
        )
        when (val result = api.getTenantUsage(page = effectivePage)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    tenantUsage = result.value.data,
                    tenantUsageHasMore = result.value.hasMore,
                    tenantUsageLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(tenantUsageLoading = false, tenantUsageError = result.error.message)
        }
    }

    // ── Error budget (S-ADMIN-6c) ───────────────────────────────────────────────

    /** Loads the per-tenant error budget for the trailing 24h window, computed purely from real outbound
     * webhook delivery outcomes — never a fabricated percentage. */
    suspend fun loadErrorBudget(page: Int? = null) {
        val effectivePage: Int = page ?: _state.value.errorBudgetPage
        _state.value = _state.value.copy(
            errorBudgetLoading = true,
            errorBudgetError = null,
            errorBudgetPage = effectivePage,
        )
        when (val result = api.getErrorBudget(page = effectivePage)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    errorBudget = result.value.data,
                    errorBudgetHasMore = result.value.hasMore,
                    errorBudgetLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(errorBudgetLoading = false, errorBudgetError = result.error.message)
        }
    }

    // ── Event-store replay (S-ADMIN-6c) ─────────────────────────────────────────

    /** Loads the registered projections a replay can target, and the real event types each subscribes to. */
    suspend fun loadReplayableProjections() {
        _state.value = _state.value.copy(replayProjectionsLoading = true, replayProjectionsError = null)
        when (val result = api.getReplayableProjections()) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(replayProjections = result.value, replayProjectionsLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    replayProjectionsLoading = false,
                    replayProjectionsError = result.error.message,
                )
        }
    }

    /** Updates the operator's in-progress replay scope. Any edit invalidates a prior preview/result — the
     * confirm control can never fire against a scope the operator has since changed since seeing its count. */
    fun setReplayScope(
        projectionName: String = _state.value.replayProjectionName,
        broadcasterId: String = _state.value.replayBroadcasterId,
        fromUtc: String = _state.value.replayFromUtc,
        toUtc: String = _state.value.replayToUtc,
        eventType: String = _state.value.replayEventType,
    ) {
        _state.value = _state.value.copy(
            replayProjectionName = projectionName,
            replayBroadcasterId = broadcasterId,
            replayFromUtc = fromUtc,
            replayToUtc = toUtc,
            replayEventType = eventType,
            replayPreview = null,
            replayPreviewError = null,
            replayResult = null,
        )
    }

    /** Counts, WITHOUT replaying anything, exactly how many events the current scope would replay — the
     * number the replay control must show before it can be confirmed. */
    suspend fun previewEventReplay() {
        val scope = _state.value
        _state.value = scope.copy(replayPreviewLoading = true, replayPreviewError = null, replayResult = null)
        when (
            val result = api.previewEventReplay(
                broadcasterId = scope.replayBroadcasterId,
                projectionName = scope.replayProjectionName,
                fromUtc = scope.replayFromUtc,
                toUtc = scope.replayToUtc,
                eventType = scope.replayEventType.takeIf { it.isNotBlank() },
            )
        ) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(replayPreview = result.value, replayPreviewLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    replayPreviewLoading = false,
                    replayPreviewError = result.error.message,
                )
        }
    }

    /** Opens the replay confirmation — reachable only once a REAL counted preview exists for this scope. */
    fun stageEventReplay() {
        if (_state.value.replayPreview != null) {
            _state.value = _state.value.copy(replayConfirmOpen = true)
        }
    }

    fun dismissEventReplay() {
        _state.value = _state.value.copy(replayConfirmOpen = false)
    }

    /** Commits the replay: re-applies exactly the previewed count to the projection, in order. Refused
     * (STALE_COUNT) if the scope's real count changed since the preview — the caller must preview again
     * rather than force a replay through against a number it never actually confirmed. */
    suspend fun confirmEventReplay() {
        val scope = _state.value
        val preview: AdminEventReplayPreview = scope.replayPreview ?: return
        _state.value = scope.copy(replayExecuting = true)
        when (
            val result = api.executeEventReplay(
                broadcasterId = scope.replayBroadcasterId,
                projectionName = scope.replayProjectionName,
                fromUtc = scope.replayFromUtc,
                toUtc = scope.replayToUtc,
                eventType = scope.replayEventType.takeIf { it.isNotBlank() },
                expectedCount = preview.matchingEventCount,
            )
        ) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    replayExecuting = false,
                    replayConfirmOpen = false,
                    replayPreview = null,
                    replayResult = result.value,
                )
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(
                    replayExecuting = false,
                    replayConfirmOpen = false,
                )
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    // ── Impersonation (admin act-as) ────────────────────────────────────────────

    /** The act-as picker's people of [broadcasterId] matching [query] (owner first); empty on failure, with the
     * failure on [AdminState.tenantMembersError]. */
    suspend fun searchTenantMembers(broadcasterId: String, query: String): List<TenantMember> {
        val members: TenantMembersApi = tenantMembersApi ?: return emptyList()
        return when (val result: ApiResult<PaginatedEnvelope<TenantMember>> = members.listMembers(broadcasterId, query)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(tenantMembersError = null)
                result.value.data
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(tenantMembersError = result.error.message)
                emptyList()
            }
        }
    }

    /**
     * Act as [subjectUserId], a person of [broadcasterId]: open an audited, time-boxed support session for
     * [justification], mint an act-as token scoped to it, and hand it to the [ActAsCoordinator], which swaps the
     * whole shell onto that user. A blank justification never reaches the network. A refused or failed mint ends
     * the support session it just opened, so a retry never leaves open grants behind. Recognized refusals land on
     * [AdminState.impersonationRefusal]. Returns whether act-as began, so the dialog closes only on success.
     */
    suspend fun impersonateTenantMember(
        broadcasterId: String,
        subjectUserId: String,
        subjectDisplayName: String,
        justification: String,
    ): Boolean {
        val trimmedJustification: String = justification.trim()
        if (trimmedJustification.isEmpty() || _state.value.impersonationInFlight) return false
        val coordinator: ActAsCoordinator =
            actAs ?: run {
                feedback.error(Res.string.admin_act_as_unavailable)
                return false
            }
        _state.value = _state.value.copy(impersonationRefusal = null, impersonationInFlight = true)
        try {
            val grant: TenantAccessGrant =
                when (val result = platformAdminApi.beginAccess(broadcasterId, BeginTenantAccessBody(justification = trimmedJustification))) {
                    is ApiResult.Ok -> result.value
                    is ApiResult.Failure -> {
                        refuse(result.error)
                        return false
                    }
                }

            val token: ImpersonationTokenDto =
                when (val result: ApiResult<ImpersonationTokenDto> = api.impersonate(subjectUserId, grant.id, trimmedJustification)) {
                    is ApiResult.Ok -> result.value
                    is ApiResult.Failure -> {
                        platformAdminApi.endAccess(grant.id)
                        refuse(result.error)
                        return false
                    }
                }

            return coordinator.begin(token, subjectDisplayName)
        } finally {
            _state.value = _state.value.copy(impersonationInFlight = false)
        }
    }

    private fun refuse(error: ApiError) {
        _state.value = _state.value.copy(impersonationRefusal = classifyImpersonationRefusal(error))
        feedback.error(Res.string.admin_action_error, error.message)
    }

    /**
     * Recognizes the refusals the confirm dialog explains specifically, on the server's error code (the
     * StatusResponseDto envelope's `code`). A policy-level denial carries no envelope, so its bare 403 status is
     * the code there.
     */
    private fun classifyImpersonationRefusal(error: ApiError): ImpersonationRefusal? = when (error.code) {
        "SESSION_REQUIRED" -> ImpersonationRefusal.NoOpenSupportSession
        "TARGET_OUTSIDE_SESSION" -> ImpersonationRefusal.TargetOutsideSession
        "FORBIDDEN", "403" -> ImpersonationRefusal.NotPermitted
        else -> null
    }

    /** Leave act-as through the [ActAsCoordinator] (restore the operator first, then revoke). */
    suspend fun exitImpersonation() {
        actAs?.exit()
    }

    private companion object {
        const val REGISTRY_CAP: Int = 50
        const val LOG_CAP: Int = 50

        /** The tabs [load] fetches for in one round-trip — all marked loading together since they share the
         * same call, and cleared together when it settles. */
        val LOAD_SECTIONS: Set<AdminSection> = setOf(
            AdminSection.Overview,
            AdminSection.Channels,
            AdminSection.Users,
            AdminSection.System,
            AdminSection.FeatureFlags,
            AdminSection.Billing,
        )

        const val FORCE_REQUIRES_JUSTIFICATION_MESSAGE: String =
            "Force publish requires a justification — describe why every tenant's copy must be overwritten."
    }
    /**
     * Loads the platform-wide spam-defence defaults. Lazy like the other heavier admin slices — it is
     * only fetched when somebody opens the tab.
     */
    suspend fun loadSpamDefaults() {
        val spam: SpamDefenseApi = spamDefenseApi ?: return
        when (val result: ApiResult<SpamDefensePolicy> = spam.platformDefaults()) {
            is ApiResult.Ok -> _state.value = _state.value.copy(spamDefaults = result.value)
            is ApiResult.Failure -> _state.value = _state.value.copy(error = result.error.message)
        }
    }

    /**
     * Saves the platform-wide defaults. Validated server-side against exactly the same ranges a channel
     * page enforces, so this cannot put every channel into a state its own editor would reject.
     */
    suspend fun saveSpamDefaults(settings: SpamDefenseSettings) {
        val spam: SpamDefenseApi = spamDefenseApi ?: return
        when (val result: ApiResult<SpamDefenseSettings> = spam.savePlatformDefaults(settings)) {
            is ApiResult.Ok ->
                _state.value =
                    _state.value.copy(
                        spamDefaults = _state.value.spamDefaults?.copy(settings = result.value),
                        error = null,
                    )
            is ApiResult.Failure -> _state.value = _state.value.copy(error = result.error.message)
        }
    }

    // ── Platform content authoring (S-ADMIN-2b) ────────────────────────────────

    /** Reads the definitions list. A failure surfaces on [AdminState.contentError] rather than leaving the
     * panel silently empty — an empty list and a failed load must never look the same. */
    suspend fun loadContentDefinitions(kind: String? = null) {
        val content: PlatformContentApi = contentApi ?: return
        _state.value = _state.value.copy(contentLoading = true, contentError = null)
        when (val result = content.listDefinitions(kind = kind)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(contentDefinitions = result.value.data, contentLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(contentLoading = false, contentError = result.error.message)
        }
    }

    /** Reads the event catalogue the `event_response` template form picks from. A failure surfaces on
     * [AdminState.contentError]; the form then has no event to offer and cannot be submitted. */
    suspend fun loadContentEventResponseTypes() {
        val content: PlatformContentApi = contentApi ?: return
        when (val result = content.eventResponseTypes()) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(contentEventResponseTypes = result.value.map { it.eventType })
            is ApiResult.Failure -> _state.value = _state.value.copy(contentError = result.error.message)
        }
    }

    /** Opens one definition's editor: the definition itself plus its FULL version history, so the owner can
     * see exactly what shipped in every prior version before drafting a new one. */
    suspend fun openContentDefinition(definitionId: String) {
        val content: PlatformContentApi = contentApi ?: return
        _state.value = _state.value.copy(
            contentDetailLoading = true,
            contentDetailError = null,
            publishPreview = null,
            lastPublishJob = null,
        )
        when (val result = content.getDefinition(definitionId)) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(
                    selectedContentDefinition = result.value,
                    contentDetailLoading = false,
                )
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    contentDetailLoading = false,
                    contentDetailError = result.error.message,
                )
        }
    }

    fun closeContentDefinition() {
        _state.value = _state.value.copy(
            selectedContentDefinition = null,
            publishPreview = null,
            lastPublishJob = null,
        )
    }

    /** Creates a new definition of the given [kind] (`command` or `widget` — the two kinds this dashboard
     * offers authoring for, see [bot.nomnomz.dashboard.core.network.PlatformContentAuthoringKinds]) with its
     * first draft version, then reloads the list and opens it. */
    suspend fun createContentDefinition(
        kind: String,
        key: String,
        displayName: String,
        description: String?,
        payloadJson: String,
    ) {
        val content: PlatformContentApi = contentApi ?: return
        val body = CreateContentDefinitionBody(
            kind = kind,
            key = key,
            displayName = displayName,
            description = description?.takeIf { it.isNotBlank() },
            payloadJson = payloadJson,
        )
        when (val result = content.createDefinition(body)) {
            is ApiResult.Ok -> {
                loadContentDefinitions()
                openContentDefinition(result.value.id)
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /** Drafts a new version on the currently-open definition. Nothing tenant-facing changes yet — a draft is
     * only a version row until it is explicitly published (§2.1). */
    suspend fun draftContentVersion(payloadJson: String) {
        val content: PlatformContentApi = contentApi ?: return
        val definitionId: String = _state.value.selectedContentDefinition?.definition?.id ?: return
        when (val result = content.draftVersion(definitionId, DraftContentVersionBody(payloadJson = payloadJson))) {
            is ApiResult.Ok -> openContentDefinition(definitionId)
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }

    /**
     * Runs the exact blast-radius query the publish will use (§2.1) and stages the result on
     * [AdminState.publishPreview] — the number the publish confirm control gates on. Selecting a different
     * mode calls this again, which is the whole point: the count is per-mode, never assumed.
     */
    suspend fun previewContentPublish(definitionId: String, versionId: String, mode: String) {
        val content: PlatformContentApi = contentApi ?: return
        _state.value = _state.value.copy(
            publishPreviewLoading = true,
            publishPreviewError = null,
            publishPreview = null,
        )
        when (val result = content.previewPublish(definitionId, versionId, PublishPreviewBody(mode = mode))) {
            is ApiResult.Ok ->
                _state.value = _state.value.copy(publishPreview = result.value, publishPreviewLoading = false)
            is ApiResult.Failure ->
                _state.value = _state.value.copy(
                    publishPreviewLoading = false,
                    publishPreviewError = result.error.message,
                )
        }
    }

    /**
     * Commits a publish. [confirmedAffectedCount] must be the exact [PublishPreview.affectedCount] the most
     * recent preview returned — the server fails closed with `PREVIEW_STALE` otherwise (§4), and the UI never
     * has a path to submit without first calling [previewContentPublish]. `force` requires a non-blank
     * [publishNote] — enforced here too, not only server-side, so a blank justification never reaches the
     * network (mirrors [impersonateTenantMember]'s own client-side gate on its justification field).
     */
    suspend fun publishContentVersion(
        definitionId: String,
        versionId: String,
        mode: String,
        publishNote: String?,
        confirmedAffectedCount: Int,
    ) {
        val content: PlatformContentApi = contentApi ?: return
        val trimmedNote: String? = publishNote?.trim()?.takeIf { it.isNotEmpty() }
        if (mode == PlatformContentPublishModes.Force && trimmedNote == null) {
            feedback.error(Res.string.admin_action_error, FORCE_REQUIRES_JUSTIFICATION_MESSAGE)
            return
        }
        _state.value = _state.value.copy(publishSubmitting = true)
        val body = PublishContentBody(
            mode = mode,
            publishNote = trimmedNote,
            confirmedPreviewAffectedCount = confirmedAffectedCount,
        )
        when (val result = content.publish(definitionId, versionId, body)) {
            is ApiResult.Ok -> {
                _state.value = _state.value.copy(publishSubmitting = false, publishPreview = null)
                // Refresh first, THEN record the job. openContentDefinition() clears lastPublishJob
                // because opening a different definition must not show a stale outcome - so setting
                // the job before the refresh loses exactly the counted confirmation the operator
                // needs after fanning a publish out across tenants.
                openContentDefinition(definitionId)
                _state.value = _state.value.copy(lastPublishJob = result.value)
            }
            is ApiResult.Failure -> {
                _state.value = _state.value.copy(publishSubmitting = false)
                feedback.error(Res.string.admin_action_error, result.error.message)
            }
        }
    }

    /** Soft-retires a definition: `RetiredAt` stops future installs and never touches already-installed
     * tenant copies (§3.1) — a truthful, non-destructive "remove from the catalogue". */
    suspend fun retireContentDefinition(definitionId: String) {
        val content: PlatformContentApi = contentApi ?: return
        when (val result = content.retireDefinition(definitionId)) {
            is ApiResult.Ok -> {
                closeContentDefinition()
                loadContentDefinitions()
            }
            is ApiResult.Failure -> feedback.error(Res.string.admin_action_error, result.error.message)
        }
    }
}

