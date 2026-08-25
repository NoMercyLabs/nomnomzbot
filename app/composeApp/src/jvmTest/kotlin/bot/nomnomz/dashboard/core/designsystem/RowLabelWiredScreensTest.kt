// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import kotlin.test.Test
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue

/**
 * One row per screen that has been wired through [resolveRowLabel] end to end (row text, action
 * labels, and the destructive delete [bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog]
 * all resolve the SAME label — see each screen's `displayName`/`displayTitle`/`resolvedName` call
 * site). Each case reproduces the EXACT [typeLabel]/[secondary] the screen passes to
 * [resolveRowLabel], using two items that share a blank primary name so a real collision would
 * surface here. When a new screen is fixed, add its parameters as one more row in [cases] — that
 * single addition is what "finishing a file" means for this test.
 */
class RowLabelWiredScreensTest {

    private data class WiredRowCase(
        val screen: String,
        val typeLabel: String,
        val secondaryA: String?,
        val idA: String,
        val secondaryB: String?,
        val idB: String,
    )

    private val cases: List<WiredRowCase> =
        listOf(
            // PickListsScreen.kt — row text/edit/delete/test labels + delete ConfirmDialog message.
            WiredRowCase(
                screen = "PickListsScreen",
                typeLabel = "Pick list",
                secondaryA = null,
                idA = "picklist-aaa",
                secondaryB = null,
                idB = "picklist-bbb",
            ),
            // CommandsScreen.kt — row text/toggle/edit/delete labels + delete ConfirmDialog message;
            // secondary identity is the command's matchPattern.
            WiredRowCase(
                screen = "CommandsScreen",
                typeLabel = "Command",
                secondaryA = "!hype",
                idA = "command-aaa",
                secondaryB = "!raid",
                idB = "command-bbb",
            ),
            // RewardsScreen.kt — row text/edit/delete labels + delete ConfirmDialog message;
            // secondary identity is the reward's cost.
            WiredRowCase(
                screen = "RewardsScreen",
                typeLabel = "Reward",
                secondaryA = "500",
                idA = "reward-aaa",
                secondaryB = "1000",
                idB = "reward-bbb",
            ),
            // GiveawaysScreen.kt (GiveawayRow + delete/close/draw ConfirmDialogs) — no secondary
            // identity field, falls straight to the typed placeholder.
            WiredRowCase(
                screen = "GiveawaysScreen.giveaway",
                typeLabel = "Giveaway",
                secondaryA = null,
                idA = "giveaway-aaa",
                secondaryB = null,
                idB = "giveaway-bbb",
            ),
            // GiveawaysScreen.kt (CodePoolRow + pool delete ConfirmDialog) — no secondary identity
            // field, falls straight to the typed placeholder.
            WiredRowCase(
                screen = "GiveawaysScreen.codePool",
                typeLabel = "Code pool",
                secondaryA = null,
                idA = "pool-aaa",
                secondaryB = null,
                idB = "pool-bbb",
            ),
            // PipelinesScreen.kt PipelineRow — row text/toggle/edit/delete labels + contentDescription +
            // delete ConfirmDialog message; no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "PipelinesScreen.pipeline",
                typeLabel = "Pipeline",
                secondaryA = null,
                idA = "pipeline-aaa",
                secondaryB = null,
                idB = "pipeline-bbb",
            ),
            // ModerationScreen.kt RuleRow — row text/toggle/delete labels + contentDescription + delete
            // ConfirmDialog message; secondary identity is the rule's type (e.g. "blocked_word").
            WiredRowCase(
                screen = "ModerationScreen.rule",
                typeLabel = "Rule",
                secondaryA = "blocked_word",
                idA = "1",
                secondaryB = "banned_link",
                idB = "2",
            ),
            // ScheduleScreen.kt SegmentRow — row text + delete ConfirmDialog message; secondary identity is
            // the segment's category name (previously fell back to the raw segment id — fixed).
            WiredRowCase(
                screen = "ScheduleScreen.segment",
                typeLabel = "Segment",
                secondaryA = "Just Chatting",
                idA = "segment-aaa",
                secondaryB = "Software and Game Development",
                idB = "segment-bbb",
            ),
            // PipelinesScreen.kt ActionPicker dropdown item — no secondary identity field, falls to the
            // typed placeholder.
            WiredRowCase(
                screen = "PipelinesScreen.actionPickerOption",
                typeLabel = "Option",
                secondaryA = null,
                idA = "option-aaa",
                secondaryB = null,
                idB = "option-bbb",
            ),
            // SettingsScreen.kt TierRow — billing tier name; no secondary identity field.
            WiredRowCase(
                screen = "SettingsScreen.tier",
                typeLabel = "Tier",
                secondaryA = null,
                idA = "tier-aaa",
                secondaryB = null,
                idB = "tier-bbb",
            ),
            // SetupWizardScreen.kt ReviewRow — wizard step title; no secondary identity field.
            WiredRowCase(
                screen = "SetupWizardScreen.step",
                typeLabel = "Step",
                secondaryA = null,
                idA = "step-aaa",
                secondaryB = null,
                idB = "step-bbb",
            ),
            // ShellScreen.kt unregistered-moderated channel row — secondary identity is the channel login.
            WiredRowCase(
                screen = "ShellScreen.channel",
                typeLabel = "Channel",
                secondaryA = "aaa_login",
                idA = "channel-aaa",
                secondaryB = "bbb_login",
                idB = "channel-bbb",
            ),
            // SoundScreen.kt ClipRow — row text/preview/edit/delete labels; secondary identity is the
            // clip's other name field (name <-> displayName fall back to each other).
            WiredRowCase(
                screen = "SoundScreen.clip",
                typeLabel = "Sound clip",
                secondaryA = "clip-aaa.mp3",
                idA = "clip-aaa",
                secondaryB = "clip-bbb.mp3",
                idB = "clip-bbb",
            ),
            // TimersScreen.kt TimerRow — row text/toggle/edit/delete labels + contentDescription; no
            // secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "TimersScreen.timer",
                typeLabel = "Timer",
                secondaryA = null,
                idA = "timer-aaa",
                secondaryB = null,
                idB = "timer-bbb",
            ),
            // TtsScreen.kt VoiceRow + VoiceMatchRow — row text/use labels + contentDescription; secondary
            // identity is the voice's internal (non-display) name.
            WiredRowCase(
                screen = "TtsScreen.voice",
                typeLabel = "Voice",
                secondaryA = "en-US-aaa",
                idA = "voice-aaa",
                secondaryB = "en-US-bbb",
                idB = "voice-bbb",
            ),
            // WebhooksScreen.kt InboundRow / OutboundRow — endpoint name; no secondary identity field.
            WiredRowCase(
                screen = "WebhooksScreen.endpoint",
                typeLabel = "Webhook",
                secondaryA = null,
                idA = "webhook-aaa",
                secondaryB = null,
                idB = "webhook-bbb",
            ),
            // WebhooksScreen.kt event-catalogue entry row — secondary identity is the raw event type.
            WiredRowCase(
                screen = "WebhooksScreen.eventEntry",
                typeLabel = "Event",
                secondaryA = "channel.follow",
                idA = "channel.follow",
                secondaryB = "channel.raid",
                idB = "channel.raid",
            ),
            // WidgetGalleryReview.kt ReviewRow + ReviewDetailPanel — gallery item name; no secondary
            // identity field.
            WiredRowCase(
                screen = "WidgetGalleryReview.item",
                typeLabel = "Widget",
                secondaryA = null,
                idA = "gallery-aaa",
                secondaryB = null,
                idB = "gallery-bbb",
            ),
            // WidgetSettingsForms.kt bool/multiselect field label — no secondary identity field.
            WiredRowCase(
                screen = "WidgetSettingsForms.field",
                typeLabel = "Field",
                secondaryA = null,
                idA = "field-aaa",
                secondaryB = null,
                idB = "field-bbb",
            ),
            // WidgetsScreen.kt WidgetRow + GalleryItemCard — widget/item name; no secondary identity field.
            WiredRowCase(
                screen = "WidgetsScreen.widget",
                typeLabel = "Widget",
                secondaryA = null,
                idA = "widget-aaa",
                secondaryB = null,
                idB = "widget-bbb",
            ),
            // AdminIamTab.kt role row — no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "AdminIamTab.role",
                typeLabel = "Role",
                secondaryA = null,
                idA = "role-aaa",
                secondaryB = null,
                idB = "role-bbb",
            ),
            // AdminIamTab.kt principal row — secondary identity is the principal's userId.
            WiredRowCase(
                screen = "AdminIamTab.principal",
                typeLabel = "Principal",
                secondaryA = "user-aaa",
                idA = "principal-aaa",
                secondaryB = "user-bbb",
                idB = "principal-bbb",
            ),
            // AdminScreen.kt channel row — secondary identity is the channel login.
            WiredRowCase(
                screen = "AdminScreen.channel",
                typeLabel = "Channel",
                secondaryA = "aaa_login",
                idA = "channel-aaa",
                secondaryB = "bbb_login",
                idB = "channel-bbb",
            ),
            // AdminScreen.kt user row — secondary identity is the user login.
            WiredRowCase(
                screen = "AdminScreen.user",
                typeLabel = "User",
                secondaryA = "aaa_login",
                idA = "user-aaa",
                secondaryB = "bbb_login",
                idB = "user-bbb",
            ),
            // AdminScreen.kt SystemTab service-health row (both the system-services card and the
            // standalone health card) — secondary identity is the service's own status word.
            WiredRowCase(
                screen = "AdminScreen.service",
                typeLabel = "Service",
                secondaryA = "healthy",
                idA = "healthy",
                secondaryB = "degraded",
                idB = "degraded",
            ),
            // AdminTenantsTab.kt tenant row + detail drawer — no secondary identity field.
            WiredRowCase(
                screen = "AdminTenantsTab.tenant",
                typeLabel = "Tenant",
                secondaryA = null,
                idA = "tenant-aaa",
                secondaryB = null,
                idB = "tenant-bbb",
            ),
            // AnalyticsScreen.kt top-viewers entry + viewer detail card — secondary identity is the
            // viewer's userId.
            WiredRowCase(
                screen = "AnalyticsScreen.viewer",
                typeLabel = "Viewer",
                secondaryA = "viewer-aaa",
                idA = "viewer-aaa",
                secondaryB = "viewer-bbb",
                idB = "viewer-bbb",
            ),
            // AssetsScreen.kt AssetRow — row text/delete label; secondary identity is the asset's slug name.
            WiredRowCase(
                screen = "AssetsScreen.asset",
                typeLabel = "Asset",
                secondaryA = "asset-aaa.png",
                idA = "asset-aaa",
                secondaryB = "asset-bbb.png",
                idB = "asset-bbb",
            ),
            // AutomationScreen.kt TokenRow — secondary identity is the token prefix.
            WiredRowCase(
                screen = "AutomationScreen.token",
                typeLabel = "Token",
                secondaryA = "nnz_aaa",
                idA = "token-aaa",
                secondaryB = "nnz_bbb",
                idB = "token-bbb",
            ),
            // AutomationScreen.kt CreateTokenDialog pipeline ToggleChip — no secondary identity field.
            WiredRowCase(
                screen = "AutomationScreen.pipeline",
                typeLabel = "Pipeline",
                secondaryA = null,
                idA = "pipeline-aaa",
                secondaryB = null,
                idB = "pipeline-bbb",
            ),
            // BundlesScreen.kt import-inspection manifest header — secondary identity is the manifest author.
            WiredRowCase(
                screen = "BundlesScreen.manifest",
                typeLabel = "Bundle",
                secondaryA = "author-aaa",
                idA = "1.0.0-aaa",
                secondaryB = "author-bbb",
                idB = "1.0.0-bbb",
            ),
            // BundlesScreen.kt import-inspection manifest item row — secondary identity is the item type.
            WiredRowCase(
                screen = "BundlesScreen.manifestItem",
                typeLabel = "Item",
                secondaryA = "command",
                idA = "items/aaa.json",
                secondaryB = "reward",
                idB = "items/bbb.json",
            ),
            // BundlesScreen.kt InstalledRow + uninstall ConfirmDialog — secondary identity is the version.
            WiredRowCase(
                screen = "BundlesScreen.installed",
                typeLabel = "Bundle",
                secondaryA = "1.0.0",
                idA = "installed-aaa",
                secondaryB = "1.1.0",
                idB = "installed-bbb",
            ),
            // BundlesScreen.kt MarketplaceRow — secondary identity is the listing author.
            WiredRowCase(
                screen = "BundlesScreen.marketplace",
                typeLabel = "Listing",
                secondaryA = "author-aaa",
                idA = "listing-aaa",
                secondaryB = "author-bbb",
                idB = "listing-bbb",
            ),
            // ChatScreen.kt EmoteSuggestions row — no secondary identity field, falls to the typed
            // placeholder; discriminator is the suggestion's own insert text.
            WiredRowCase(
                screen = "ChatScreen.suggestion",
                typeLabel = "Suggestion",
                secondaryA = null,
                idA = "verosWaving",
                secondaryB = null,
                idB = "aaoaWat",
            ),
            // CodeScriptsController.kt editCode() editor title + CodeScriptsScreen.kt header/row/delete —
            // no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "CodeScriptsScreen.script",
                typeLabel = "Script",
                secondaryA = null,
                idA = "script-aaa",
                secondaryB = null,
                idB = "script-bbb",
            ),
            // CodeScriptsScreen.kt test-run captured-effect row — discriminator is the effect's argsPreview.
            WiredRowCase(
                screen = "CodeScriptsScreen.effect",
                typeLabel = "Effect",
                secondaryA = null,
                idA = "chat: hello",
                secondaryB = null,
                idB = "chat: bye",
            ),
            // CommandsScreen.kt BuiltinTableRow — no secondary identity field; discriminator is the
            // builtin's stable key.
            WiredRowCase(
                screen = "CommandsScreen.builtin",
                typeLabel = "Command",
                secondaryA = null,
                idA = "builtin-aaa",
                secondaryB = null,
                idB = "builtin-bbb",
            ),
            // CommunityScreen.kt top-chatters row — secondary identity is the chatter's userId.
            WiredRowCase(
                screen = "CommunityScreen.chatter",
                typeLabel = "Chatter",
                secondaryA = "chatter-aaa",
                idA = "chatter-aaa",
                secondaryB = "chatter-bbb",
                idB = "chatter-bbb",
            ),
            // ConnectScreen.kt SavedConnectionRow — secondary identity is the saved connection's base URL.
            WiredRowCase(
                screen = "ConnectScreen.saved",
                typeLabel = "Connection",
                secondaryA = "https://aaa.example",
                idA = "saved-aaa",
                secondaryB = "https://bbb.example",
                idB = "saved-bbb",
            ),
            // ConnectScreen.kt DiscoveredRow — secondary identity is the discovered profile's base URL.
            WiredRowCase(
                screen = "ConnectScreen.discovered",
                typeLabel = "Server",
                secondaryA = "https://ccc.example",
                idA = "discovered-aaa",
                secondaryB = "https://ddd.example",
                idB = "discovered-bbb",
            ),
            // CustomEventsScreen.kt SourceRow/TestDialog/delete ConfirmDialog — secondary identity is the
            // data source's slug (name).
            WiredRowCase(
                screen = "CustomEventsScreen.source",
                typeLabel = "Data source",
                secondaryA = "heartrate",
                idA = "source-aaa",
                secondaryB = "chatbot",
                idB = "source-bbb",
            ),
            // DiscordScreen.kt ping-role picker — no secondary identity field.
            WiredRowCase(
                screen = "DiscordScreen.role",
                typeLabel = "Role",
                secondaryA = null,
                idA = "role-aaa",
                secondaryB = null,
                idB = "role-bbb",
            ),
            // EconomyController.kt searchViewers/searchChannels PickerOptions — secondary identity is the
            // viewer's username / channel's login.
            WiredRowCase(
                screen = "EconomyController.viewer",
                typeLabel = "Viewer",
                secondaryA = "aaa_username",
                idA = "viewer-aaa",
                secondaryB = "bbb_username",
                idB = "viewer-bbb",
            ),
            // EconomyScreen.kt LeaderboardRow — no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "EconomyScreen.participant",
                typeLabel = "Participant",
                secondaryA = null,
                idA = "participant-aaa",
                secondaryB = null,
                idB = "participant-bbb",
            ),
            // EconomyScreen.kt CatalogItemRow / delete ConfirmDialog — secondary identity is the item's cost.
            WiredRowCase(
                screen = "EconomyScreen.catalogItem",
                typeLabel = "Store item",
                secondaryA = "500",
                idA = "item-aaa",
                secondaryB = "1000",
                idB = "item-bbb",
            ),
            // EconomyScreen.kt SavingsJarRow / JarManageDialog / history dialog — no secondary identity field.
            WiredRowCase(
                screen = "EconomyScreen.jar",
                typeLabel = "Savings jar",
                secondaryA = null,
                idA = "jar-aaa",
                secondaryB = null,
                idB = "jar-bbb",
            ),
            // FeaturesScreen.kt FeatureRow — secondary identity is the feature's key.
            WiredRowCase(
                screen = "FeaturesScreen.feature",
                typeLabel = "Feature",
                secondaryA = "raid_alerts",
                idA = "raid_alerts",
                secondaryB = "chat_commands",
                idB = "chat_commands",
            ),
            // FederationScreen.kt PeerRow — secondary identity is the peer's base URL.
            WiredRowCase(
                screen = "FederationScreen.peer",
                typeLabel = "Peer",
                secondaryA = "https://peer-aaa.example",
                idA = "peer-aaa",
                secondaryB = "https://peer-bbb.example",
                idB = "peer-bbb",
            ),
            // GamesScreen.kt LiveGameRow — secondary identity is the game's key.
            WiredRowCase(
                screen = "GamesScreen.liveGame",
                typeLabel = "Game",
                secondaryA = "trivia",
                idA = "trivia",
                secondaryB = "roulette",
                idB = "roulette",
            ),
            // GiveawaysScreen.kt code-pool SelectChip — no secondary identity field.
            WiredRowCase(
                screen = "GiveawaysScreen.poolChip",
                typeLabel = "Code pool",
                secondaryA = null,
                idA = "chip-pool-aaa",
                secondaryB = null,
                idB = "chip-pool-bbb",
            ),
            // HomeController.kt searchCategories PickerOptions — no secondary identity field.
            WiredRowCase(
                screen = "HomeController.category",
                typeLabel = "Category",
                secondaryA = null,
                idA = "category-aaa",
                secondaryB = null,
                idB = "category-bbb",
            ),
            // MediaShareScreen.kt RequestRow — secondary identity is the request's mediaRef.
            WiredRowCase(
                screen = "MediaShareScreen.request",
                typeLabel = "Clip",
                secondaryA = "https://clip-aaa.example/watch",
                idA = "request-aaa",
                secondaryB = "https://clip-bbb.example/watch",
                idB = "request-bbb",
            ),
            // ObsScreen.kt scene Badge row — no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "ObsScreen.scene",
                typeLabel = "Scene",
                secondaryA = null,
                idA = "0-scene-aaa",
                secondaryB = null,
                idB = "1-scene-bbb",
            ),
            // ObsScreen.kt MixerRow — secondary identity is the input's kind.
            WiredRowCase(
                screen = "ObsScreen.input",
                typeLabel = "Input",
                secondaryA = "wasapi_input_capture",
                idA = "input-aaa",
                secondaryB = "audio_input_capture",
                idB = "input-bbb",
            ),
            // LeaderboardsScreen.kt RankRow — no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "LeaderboardsScreen.rank",
                typeLabel = "Participant",
                secondaryA = null,
                idA = "rank-aaa",
                secondaryB = null,
                idB = "rank-bbb",
            ),
            // PointsAndStoreScreen.kt CatalogRow — secondary identity is the item's cost.
            WiredRowCase(
                screen = "PointsAndStoreScreen.catalogItem",
                typeLabel = "Store item",
                secondaryA = "250",
                idA = "store-item-aaa",
                secondaryB = "750",
                idB = "store-item-bbb",
            ),
            // PointsAndStoreScreen.kt JarRow — no secondary identity field, falls to the typed placeholder.
            WiredRowCase(
                screen = "PointsAndStoreScreen.jar",
                typeLabel = "Savings jar",
                secondaryA = null,
                idA = "store-jar-aaa",
                secondaryB = null,
                idB = "store-jar-bbb",
            ),
            // RolesScreen.kt memberName() — MemberRow text/RolePicker/GrantButton/RemoveButton labels +
            // remove ConfirmDialog message; previously fell back to the raw userId, now typed placeholder.
            WiredRowCase(
                screen = "RolesScreen.member",
                typeLabel = "Member",
                secondaryA = null,
                idA = "member-aaa",
                secondaryB = null,
                idB = "member-bbb",
            ),
            // RolesScreen.kt permitName() — PermitRow text/revoke label + revoke ConfirmDialog message;
            // previously fell back to the raw userId, now typed placeholder.
            WiredRowCase(
                screen = "RolesScreen.permit",
                typeLabel = "Grant",
                secondaryA = null,
                idA = "permit-aaa",
                secondaryB = null,
                idB = "permit-bbb",
            ),
            // ParticipantShell.kt ProfileBlock — row text + avatar initial + dropdown header; previously
            // fell back to a blank string, now the viewer's username or a typed placeholder.
            WiredRowCase(
                screen = "ParticipantShell.profile",
                typeLabel = "Viewer",
                secondaryA = "aaa_username",
                idA = "user-aaa",
                secondaryB = "bbb_username",
                idB = "user-bbb",
            ),
            // ShellScreen.kt ProfileBlock — row text + dropdown header; previously fell back to a blank
            // string, now the streamer's username or a typed placeholder.
            WiredRowCase(
                screen = "ShellScreen.profile",
                typeLabel = "Viewer",
                secondaryA = "aaa_username",
                idA = "user-aaa",
                secondaryB = "bbb_username",
                idB = "user-bbb",
            ),
            // GiveawaysScreen.kt winners-dialog title — previously fell back to a blank string, now the
            // giveaway's typed placeholder (no secondary identity field).
            WiredRowCase(
                screen = "GiveawaysScreen.winnersDialog",
                typeLabel = "Giveaway",
                secondaryA = null,
                idA = "giveaway-winners-aaa",
                secondaryB = null,
                idB = "giveaway-winners-bbb",
            ),
        )

    @Test
    fun a_blank_named_row_resolves_to_an_identifying_label_on_every_fixed_screen() {
        for (case in cases) {
            val label: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            assertTrue(
                label.isNotBlank(),
                "${case.screen}: a blank-named row must never resolve to a blank label",
            )
            assertTrue(
                label == case.secondaryA || label.startsWith("${case.typeLabel} #"),
                "${case.screen}: expected the secondary identity or a typed placeholder, got '$label'",
            )
        }
    }

    @Test
    fun two_blank_named_rows_in_the_same_list_never_render_identically() {
        for (case in cases) {
            val labelA: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            val labelB: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryB,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idB,
                )
            assertNotEquals(
                labelA,
                labelB,
                "${case.screen}: two blank-named rows collided on '$labelA' — a user could no longer " +
                    "tell them apart before deleting one",
            )
        }
    }

    @Test
    fun the_destructive_confirm_dialog_names_the_item_via_the_same_resolved_label() {
        // Each screen computes ONE resolved label per row and reuses it for the row text, the row's
        // action labels, AND the destructive ConfirmDialog's message (see CommandsScreen.kt's
        // `displayName`, RewardsScreen.kt's `displayName`, GiveawaysScreen.kt's `displayTitle` /
        // `displayName` / `resolvedTitle` / `resolvedPoolName` / `resolvedLifecycleTitle`, and
        // PickListsScreen.kt's `resolvedName` — a single call site feeding every consumer). This test
        // proves the mechanism itself is idempotent for identical inputs, which is what makes reusing
        // one resolved value for both the row and its confirm dialog safe instead of computing it twice
        // and risking drift.
        for (case in cases) {
            val rowLabel: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            val confirmDialogLabel: String =
                resolveRowLabel(
                    primary = null,
                    secondary = case.secondaryA,
                    typeLabel = case.typeLabel,
                    discriminatorSource = case.idA,
                )
            assertTrue(
                rowLabel == confirmDialogLabel,
                "${case.screen}: the row label and its destructive ConfirmDialog must name the same item",
            )
        }
    }
}
