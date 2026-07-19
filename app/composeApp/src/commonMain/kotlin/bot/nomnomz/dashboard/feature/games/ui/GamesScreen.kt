// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.games.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text

import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.network.GamePlayEntry
import bot.nomnomz.dashboard.core.network.GameSession
import bot.nomnomz.dashboard.core.network.GameSummary
import bot.nomnomz.dashboard.core.network.LiveGameCatalogEntry
import bot.nomnomz.dashboard.feature.games.state.GamesController
import bot.nomnomz.dashboard.feature.games.state.GamesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_games
import nomnomzbot.composeapp.generated.resources.games_18plus
import nomnomzbot.composeapp.generated.resources.games_action_error
import nomnomzbot.composeapp.generated.resources.games_cooldown
import nomnomzbot.composeapp.generated.resources.games_dialog_18plus_label
import nomnomzbot.composeapp.generated.resources.games_dialog_advanced_section
import nomnomzbot.composeapp.generated.resources.games_dialog_cancel
import nomnomzbot.composeapp.generated.resources.games_dialog_config_add
import nomnomzbot.composeapp.generated.resources.games_dialog_config_key_hint
import nomnomzbot.composeapp.generated.resources.games_dialog_config_remove
import nomnomzbot.composeapp.generated.resources.games_dialog_config_value_hint
import nomnomzbot.composeapp.generated.resources.games_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.games_dialog_house_edge_label
import nomnomzbot.composeapp.generated.resources.games_dialog_limits_section
import nomnomzbot.composeapp.generated.resources.games_dialog_max_bet_label
import nomnomzbot.composeapp.generated.resources.games_dialog_max_plays_label
import nomnomzbot.composeapp.generated.resources.games_dialog_min_bet_label
import nomnomzbot.composeapp.generated.resources.games_dialog_odds_section
import nomnomzbot.composeapp.generated.resources.games_dialog_payout_label
import nomnomzbot.composeapp.generated.resources.games_dialog_permission_label
import nomnomzbot.composeapp.generated.resources.games_dialog_save
import nomnomzbot.composeapp.generated.resources.games_dialog_title
import nomnomzbot.composeapp.generated.resources.games_dialog_win_chance_label
import nomnomzbot.composeapp.generated.resources.games_perm_broadcaster
import nomnomzbot.composeapp.generated.resources.games_perm_everyone
import nomnomzbot.composeapp.generated.resources.games_perm_moderator
import nomnomzbot.composeapp.generated.resources.games_perm_subscriber
import nomnomzbot.composeapp.generated.resources.games_perm_vip
import nomnomzbot.composeapp.generated.resources.games_history_empty
import nomnomzbot.composeapp.generated.resources.games_history_title
import nomnomzbot.composeapp.generated.resources.games_disabled
import nomnomzbot.composeapp.generated.resources.games_edit_action
import nomnomzbot.composeapp.generated.resources.games_empty
import nomnomzbot.composeapp.generated.resources.games_enabled
import nomnomzbot.composeapp.generated.resources.games_error
import nomnomzbot.composeapp.generated.resources.games_loading
import nomnomzbot.composeapp.generated.resources.games_retry
import nomnomzbot.composeapp.generated.resources.games_row_description
import nomnomzbot.composeapp.generated.resources.games_toggle_action
import nomnomzbot.composeapp.generated.resources.games_section_gambling
import nomnomzbot.composeapp.generated.resources.games_section_gambling_hint
import nomnomzbot.composeapp.generated.resources.games_section_live
import nomnomzbot.composeapp.generated.resources.games_section_live_hint
import nomnomzbot.composeapp.generated.resources.games_live_start
import nomnomzbot.composeapp.generated.resources.games_live_keyword
import nomnomzbot.composeapp.generated.resources.games_live_players
import nomnomzbot.composeapp.generated.resources.games_live_entry_fee
import nomnomzbot.composeapp.generated.resources.games_live_active_title
import nomnomzbot.composeapp.generated.resources.games_live_participants
import nomnomzbot.composeapp.generated.resources.games_live_cancel
import nomnomzbot.composeapp.generated.resources.games_live_requires_mod
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject

// The Games page (economy.md §3.5): the channel's configured mini-games — every game is real config from
// [GamesController] (the backend sources it from the channel's game config; no fabricated games). Games are a
// fixed catalog of built-in types, so this is a MANAGEMENT (not create/delete) surface: each row toggles its
// enabled flag inline and opens a config dialog (bet limits, cooldown, 18+ gate). Every write routes back through
// the controller, which re-lists after each success so the page reflects the backend. The screen loads on first
// composition and offers a retry on failure.
@Composable
fun GamesScreen(controller: GamesController, role: ManagementRole?) {
    val state: GamesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Games gates every write control at its single Editor manage floor
    // (frontend-ia.md §3, economy.md §3.5). A caller below it sees the catalog but every toggle/edit control
    // renders disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Games)

    // Starting/cancelling a LIVE round floors at Moderator (games:session:start/cancel), a rung below the page's
    // Editor config floor — so a Moderator who can't tune the odds CAN still run a round. The page is Moderator-read
    // anyway, so this is Allowed for essentially every viewer of it; below Moderator it renders disabled with reason.
    val liveManage: ManageDecision =
        if (role != null && role.level >= ManagementRole.Moderator.level) ManageDecision.Allowed
        else ManageDecision.Denied(stringResource(Res.string.games_live_requires_mod))

    // The config dialog target: null = closed, a game = open and editing that game's config.
    var editing: GameSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    // Keep the active-session card live (participants climbing, status changes) while a round is in its join/run
    // phase, without a full page reload.
    val activeStatus: String? = (state as? GamesState.Ready)?.activeSession?.status
    if (activeStatus == "Lobby" || activeStatus == "Running") {
        LaunchedEffect(Unit) {
            while (true) {
                delay(2000)
                controller.refreshActiveSession()
            }
        }
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: GamesState = state) {
            is GamesState.Loading -> CenteredMessage(stringResource(Res.string.games_loading))
            is GamesState.Empty -> CenteredMessage(stringResource(Res.string.games_empty))
            is GamesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is GamesState.Ready ->
                ManagedContent(
                    games = current.games,
                    history = current.history,
                    liveCatalog = current.liveCatalog,
                    activeSession = current.activeSession,
                    actionError = current.actionError,
                    manage = manage,
                    liveManage = liveManage,
                    onToggle = { game, enabled ->
                        scope.launch { controller.toggleGame(game, enabled) }
                    },
                    onEdit = { game -> editing = game },
                    onStartLive = { gameKey -> scope.launch { controller.startLiveGame(gameKey) } },
                    onCancelLive = { sessionId -> scope.launch { controller.cancelLiveGame(sessionId) } },
                )
        }
    }

    editing?.let { game ->
        GameConfigDialog(
            game = game,
            onDismiss = { editing = null },
            onSave = { edit ->
                editing = null
                scope.launch {
                    controller.updateGameConfig(
                        game = game,
                        minBet = edit.minBet,
                        maxBet = edit.maxBet,
                        cooldownSeconds = edit.cooldownSeconds,
                        requires18Plus = edit.requires18Plus,
                        winChancePercent = edit.winChancePercent,
                        payoutMultiplier = edit.payoutMultiplier,
                        houseEdgePercent = edit.houseEdgePercent,
                        maxPlaysPerStream = edit.maxPlaysPerStream,
                        permission = edit.permission,
                        config = edit.config,
                    )
                }
            },
        )
    }
}

// The list with an optional write-failure banner above it. The catalog header is omitted (no create action — the
// catalog is fixed); the history section follows beneath with the first page of recent plays.
@Composable
private fun ManagedContent(
    games: List<GameSummary>,
    history: List<GamePlayEntry>,
    liveCatalog: List<LiveGameCatalogEntry>,
    activeSession: GameSession?,
    actionError: String?,
    manage: ManageDecision,
    liveManage: ManageDecision,
    onToggle: (GameSummary, Boolean) -> Unit,
    onEdit: (GameSummary) -> Unit,
    onStartLive: (gameKey: String) -> Unit,
    onCancelLive: (sessionId: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        item(key = "page-header") { PageHeader(title = stringResource(Res.string.shell_nav_games)) }
        actionError?.let { detail ->
            item(key = "action-error") { ActionErrorBanner(message = stringResource(Res.string.games_action_error, detail)) }
        }

        // ── Interactive overlay games (live, join-by-keyword) ────────────────────────────────────────────────
        // The flashy half: catalog rounds a streamer/mod starts and viewers join with a keyword. Rendered first so
        // it's front-and-centre; only shown when the backend discovered live games.
        if (liveCatalog.isNotEmpty()) {
            item(key = "live-header") {
                SectionLabel(
                    title = stringResource(Res.string.games_section_live),
                    hint = stringResource(Res.string.games_section_live_hint),
                )
            }
            activeSession?.let { session ->
                item(key = "live-active") {
                    ActiveSessionCard(session = session, liveManage = liveManage, onCancel = { onCancelLive(session.id) })
                }
            }
            item(key = "live-catalog") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        liveCatalog.forEachIndexed { index, entry ->
                            LiveGameRow(
                                entry = entry,
                                liveManage = liveManage,
                                // A round is already running → starting another is blocked server-side (D7); disable
                                // the start button while any session is active so the UI reflects that.
                                sessionActive = activeSession != null,
                                onStart = { onStartLive(entry.gameKey) },
                            )
                            if (index < liveCatalog.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }

        // ── Gambling minigames (instant, !coinflip-style — these ARE commands you tune here) ─────────────────
        if (games.isNotEmpty()) {
            item(key = "gambling-header") {
                SectionLabel(
                    title = stringResource(Res.string.games_section_gambling),
                    hint = stringResource(Res.string.games_section_gambling_hint),
                )
            }
            item(key = "games-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        games.forEachIndexed { index, game ->
                            GameRow(
                                game = game,
                                manage = manage,
                                onToggle = { enabled -> onToggle(game, enabled) },
                                onEdit = { onEdit(game) },
                            )
                            if (index < games.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        item(key = "history-header") {
            Text(
                text = stringResource(Res.string.games_history_title),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(horizontal = spacing.s1, vertical = spacing.s2),
            )
        }
        if (history.isEmpty()) {
            item(key = "history-empty") {
                Text(
                    text = stringResource(Res.string.games_history_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.padding(horizontal = spacing.s1),
                )
            }
        } else {
            item(key = "history-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        history.forEachIndexed { index, play ->
                            HistoryRow(play = play)
                            if (index < history.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun HistoryRow(play: GamePlayEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val netColor = if (play.netResult >= 0) tokens.primary else tokens.destructive
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = play.outcome.replaceFirstChar { it.uppercase() },
            style = typography.sm,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = "${play.betAmount} → ${play.payoutAmount}",
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = if (play.netResult >= 0) "+${play.netResult}" else "${play.netResult}",
            style = typography.sm,
            color = netColor,
            modifier = Modifier.padding(start = spacing.s3),
        )
    }
}

// A titled section divider with a one-line explanation — clarifies gambling minigames (commands) vs live overlay
// games (started + joined by keyword).
@Composable
private fun SectionLabel(title: String, hint: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.padding(horizontal = spacing.s1, vertical = spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(text = title, style = typography.lg, color = tokens.foreground)
        Text(text = hint, style = typography.sm, color = tokens.mutedForeground)
    }
}

// The running-round card: the game, its status, the live participant count (climbing as chatters join), and a
// cancel action (refunds every entry fee). Gated at the Moderator live floor.
@Composable
private fun ActiveSessionCard(session: GameSession, liveManage: ManageDecision, onCancel: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(
                modifier = Modifier.weight(1f),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = stringResource(Res.string.games_live_active_title, session.gameType),
                    style = typography.base,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "${session.status} · " +
                        stringResource(Res.string.games_live_participants, session.participantCount),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
            ManageGate(decision = liveManage) { enabled ->
                Button(
                    onClick = onCancel,
                    enabled = enabled,
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                ) { Text(text = stringResource(Res.string.games_live_cancel), maxLines = 1) }
            }
        }
    }
}

// One live-game catalog row: the game name, its reserved join keyword(s) (read-only), the player bounds and
// entry-fee flag, and a Start button (disabled while any round is active — one session at a time, D7).
@Composable
private fun LiveGameRow(
    entry: LiveGameCatalogEntry,
    liveManage: ManageDecision,
    sessionActive: Boolean,
    onStart: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val keywords: String = entry.inputKeywords.joinToString(" ") { "!$it" }
    val playersLabel: String =
        stringResource(Res.string.games_live_players, entry.minPlayers, entry.maxPlayers)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = entry.displayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (keywords.isNotBlank()) {
                Text(
                    text = stringResource(Res.string.games_live_keyword, keywords),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            Text(
                text = playersLabel,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        if (entry.requiresEntryFee) {
            Badge(
                label = stringResource(Res.string.games_live_entry_fee),
                background = tokens.secondary,
                foreground = tokens.secondaryForeground,
            )
        }
        ManageGate(decision = liveManage) { enabled ->
            Button(onClick = onStart, enabled = enabled && !sessionActive, size = ButtonSize.Sm) {
                Text(text = stringResource(Res.string.games_live_start), maxLines = 1)
            }
        }
    }
}

@Composable
private fun GameRow(
    game: GameSummary,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    onEdit: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusLabel: String =
        if (game.isEnabled) stringResource(Res.string.games_enabled)
        else stringResource(Res.string.games_disabled)
    val cooldownLabel: String? =
        if (game.cooldownSeconds > 0)
            stringResource(Res.string.games_cooldown, game.cooldownSeconds)
        else null
    // One node for screen readers describing the game: "coinflip, Enabled, 30s cooldown".
    val rowDescription: String =
        stringResource(
            Res.string.games_row_description,
            game.gameType,
            statusLabel,
            cooldownLabel ?: "",
        )
    val toggleLabel: String = stringResource(Res.string.games_toggle_action, game.gameType)
    val editLabel: String = stringResource(Res.string.games_edit_action, game.gameType)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = game.gameType,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            if (game.category.isNotBlank()) {
                Text(
                    text = game.category,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            if (cooldownLabel != null) {
                Text(
                    text = cooldownLabel,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
        }
        if (game.requires18Plus) {
            Badge(
                label = stringResource(Res.string.games_18plus),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = game.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
        }
    }
}

// The full submitted config, bundled so the save callback has one clean parameter.
private data class GameConfigEdit(
    val minBet: Long?,
    val maxBet: Long?,
    val cooldownSeconds: Int,
    val requires18Plus: Boolean,
    val winChancePercent: Double?,
    val payoutMultiplier: Double?,
    val houseEdgePercent: Double?,
    val maxPlaysPerStream: Int?,
    val permission: String,
    val config: JsonObject?,
)

// The per-game config editor — the WHOLE tunable surface, grouped: limits (bet bounds, cooldown, per-stream play
// cap, 18+ gate), odds & payout (win chance %, payout multiplier, house edge %), the minimum-role permission
// (role NAMES only, a CommunityStanding), and the per-game tuning knobs (ConfigJson) as a key/value editor
// prefilled with the game's current keys (win_radius, success_chance, max_multiplier, …). A blank number field
// means "no limit"/"unset" (null); non-blank fields must parse to a non-negative value. Percent fields are
// clamped to 0–100. Save is disabled while any field is invalid or min bet exceeds max bet, so an invalid config
// can never be sent. Only the game type + category are fixed (the address) — carried back unchanged.
@Composable
private fun GameConfigDialog(
    game: GameSummary,
    onDismiss: () -> Unit,
    onSave: (GameConfigEdit) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var minBet: String by remember { mutableStateOf(game.minBet?.toString().orEmpty()) }
    var maxBet: String by remember { mutableStateOf(game.maxBet?.toString().orEmpty()) }
    var cooldown: String by remember { mutableStateOf(game.cooldownSeconds.toString()) }
    var maxPlays: String by remember { mutableStateOf(game.maxPlaysPerStream?.toString().orEmpty()) }
    var requires18Plus: Boolean by remember { mutableStateOf(game.requires18Plus) }
    var winChance: String by remember { mutableStateOf(game.winChancePercent?.let { formatDecimal(it) }.orEmpty()) }
    var payout: String by remember { mutableStateOf(game.payoutMultiplier?.let { formatDecimal(it) }.orEmpty()) }
    var houseEdge: String by remember { mutableStateOf(game.houseEdgePercent?.let { formatDecimal(it) }.orEmpty()) }
    var permission: String by remember {
        mutableStateOf(game.permission.ifBlank { "Everyone" })
    }
    var permMenuOpen: Boolean by remember { mutableStateOf(false) }
    // The ConfigJson knobs as editable rows, seeded from the game's current keys. Replaced by index on edit so the
    // SnapshotStateList triggers recomposition.
    val configEntries = remember { mutableStateListOf<Pair<String, String>>().apply { addAll(configToEntries(game.config)) } }

    // A blank bet/play field is a valid "no limit"; a non-blank field must parse to a non-negative whole number.
    val minBetValue: Long? = minBet.toLongOrNull()
    val maxBetValue: Long? = maxBet.toLongOrNull()
    val cooldownValue: Int? = cooldown.ifBlank { "0" }.toIntOrNull()
    val maxPlaysValue: Int? = maxPlays.toIntOrNull()
    val winChanceValue: Double? = winChance.toDoubleOrNull()
    val payoutValue: Double? = payout.toDoubleOrNull()
    val houseEdgeValue: Double? = houseEdge.toDoubleOrNull()

    val minBetValid: Boolean = minBet.isBlank() || (minBetValue != null && minBetValue >= 0)
    val maxBetValid: Boolean = maxBet.isBlank() || (maxBetValue != null && maxBetValue >= 0)
    val cooldownValid: Boolean = cooldownValue != null && cooldownValue >= 0
    val maxPlaysValid: Boolean = maxPlays.isBlank() || (maxPlaysValue != null && maxPlaysValue >= 0)
    val winChanceValid: Boolean = winChance.isBlank() || (winChanceValue != null && winChanceValue in 0.0..100.0)
    val payoutValid: Boolean = payout.isBlank() || (payoutValue != null && payoutValue >= 0.0)
    val houseEdgeValid: Boolean = houseEdge.isBlank() || (houseEdgeValue != null && houseEdgeValue in 0.0..100.0)
    val rangeValid: Boolean = minBetValue == null || maxBetValue == null || minBetValue <= maxBetValue
    val canSave: Boolean =
        minBetValid && maxBetValid && cooldownValid && maxPlaysValid &&
            winChanceValid && payoutValid && houseEdgeValid && rangeValid

    val eighteenPlusLabel: String = stringResource(Res.string.games_dialog_18plus_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.games_dialog_title, game.gameType)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                DialogSectionLabel(stringResource(Res.string.games_dialog_limits_section))
                AppTextField(
                    value = minBet,
                    onValueChange = { minBet = it },
                    isError = !minBetValid || !rangeValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_min_bet_label),
                )
                AppTextField(
                    value = maxBet,
                    onValueChange = { maxBet = it },
                    isError = !maxBetValid || !rangeValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_max_bet_label),
                )
                AppTextField(
                    value = cooldown,
                    onValueChange = { cooldown = it },
                    isError = !cooldownValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_cooldown_label),
                )
                AppTextField(
                    value = maxPlays,
                    onValueChange = { maxPlays = it },
                    isError = !maxPlaysValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_max_plays_label),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = eighteenPlusLabel, color = tokens.cardForeground)
                    Switch(
                        checked = requires18Plus,
                        onCheckedChange = { requires18Plus = it },
                        modifier = Modifier.semantics { contentDescription = eighteenPlusLabel },
                    )
                }

                // Minimum role that can play — role NAMES only, never the numeric ladder value (house rule).
                PickerField(
                    label = stringResource(Res.string.games_dialog_permission_label),
                    value = permissionLabel(permission),
                    expanded = permMenuOpen,
                    onExpandedChange = { permMenuOpen = it },
                ) {
                    GamePermissionRungs.forEach { (name, res) ->
                        DropdownMenuItem(
                            text = { Text(stringResource(res), color = tokens.cardForeground) },
                            onClick = {
                                permission = name
                                permMenuOpen = false
                            },
                        )
                    }
                }

                DialogSectionLabel(stringResource(Res.string.games_dialog_odds_section))
                AppTextField(
                    value = winChance,
                    onValueChange = { winChance = it },
                    isError = !winChanceValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_win_chance_label),
                )
                AppTextField(
                    value = payout,
                    onValueChange = { payout = it },
                    isError = !payoutValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_payout_label),
                )
                AppTextField(
                    value = houseEdge,
                    onValueChange = { houseEdge = it },
                    isError = !houseEdgeValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.games_dialog_house_edge_label),
                )

                DialogSectionLabel(stringResource(Res.string.games_dialog_advanced_section))
                configEntries.forEachIndexed { index, entry ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                    ) {
                        AppTextField(
                            value = entry.first,
                            onValueChange = { configEntries[index] = it to entry.second },
                            modifier = Modifier.weight(1f),
                            label = stringResource(Res.string.games_dialog_config_key_hint),
                        )
                        AppTextField(
                            value = entry.second,
                            onValueChange = { configEntries[index] = entry.first to it },
                            modifier = Modifier.weight(1f),
                            label = stringResource(Res.string.games_dialog_config_value_hint),
                        )
                        GlyphButton(
                            imageVector = EditGlyph,
                            label = stringResource(Res.string.games_dialog_config_remove, entry.first),
                            onClick = { configEntries.removeAt(index) },
                        )
                    }
                }
                TextButton(onClick = { configEntries.add("" to "") }) {
                    Text(text = stringResource(Res.string.games_dialog_config_add), color = tokens.primary)
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSave(
                        GameConfigEdit(
                            minBet = minBetValue,
                            maxBet = maxBetValue,
                            cooldownSeconds = cooldownValue ?: 0,
                            requires18Plus = requires18Plus,
                            winChancePercent = winChanceValue,
                            payoutMultiplier = payoutValue,
                            houseEdgePercent = houseEdgeValue,
                            maxPlaysPerStream = maxPlaysValue,
                            permission = permission,
                            config = entriesToConfig(configEntries),
                        )
                    )
                },
                enabled = canSave,
            ) {
                Text(
                    text = stringResource(Res.string.games_dialog_save),
                    color = if (canSave) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.games_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// A small group heading inside the config dialog.
@Composable
private fun DialogSectionLabel(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Text(text = text, style = typography.sm, color = tokens.mutedForeground, maxLines = 1)
}

// A read-only field that opens a themed dropdown when clicked (the shared select pattern — an AppTextField shows
// the current value, its Box anchors the DropdownMenu).
@Composable
private fun PickerField(
    label: String,
    value: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    items: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    Box {
        AppTextField(
            value = value,
            onValueChange = {},
            modifier = Modifier.fillMaxWidth().clickable { onExpandedChange(true) },
            label = label,
        )
        DropdownMenu(expanded = expanded, onDismissRequest = { onExpandedChange(false) }, content = items)
    }
}

// The minimum-role options — the CommunityStanding names the backend's game Permission accepts, mapped to their
// localized role NAMES (never a numeric ladder value). The stored value is the enum name string.
private val GamePermissionRungs: List<Pair<String, StringResource>> =
    listOf(
        "Everyone" to Res.string.games_perm_everyone,
        "Subscriber" to Res.string.games_perm_subscriber,
        "Vip" to Res.string.games_perm_vip,
        "Moderator" to Res.string.games_perm_moderator,
        "Broadcaster" to Res.string.games_perm_broadcaster,
    )

@Composable
private fun permissionLabel(name: String): String =
    stringResource(
        GamePermissionRungs.firstOrNull { it.first.equals(name, ignoreCase = true) }?.second
            ?: Res.string.games_perm_everyone
    )

// Render the game's opaque ConfigJson as editable key/value rows: each primitive's raw content as a string.
private fun configToEntries(config: JsonObject?): List<Pair<String, String>> =
    config?.map { (key, value) -> key to ((value as? JsonPrimitive)?.content ?: value.toString()) } ?: emptyList()

// Rebuild a ConfigJson object from the edited rows, dropping blank keys. Values are typed back to the tightest
// JSON primitive (whole number → long, decimal → double, true/false → boolean, otherwise string) so a game that
// reads a numeric knob still sees a number. Null when no keys remain (the game falls back to its in-code defaults).
private fun entriesToConfig(entries: List<Pair<String, String>>): JsonObject? {
    val clean: List<Pair<String, String>> = entries.filter { it.first.isNotBlank() }
    if (clean.isEmpty()) return null
    return buildJsonObject {
        clean.forEach { (key, value) -> put(key, parseConfigValue(value)) }
    }
}

private fun parseConfigValue(raw: String): JsonElement {
    val trimmed: String = raw.trim()
    trimmed.toLongOrNull()?.let { return JsonPrimitive(it) }
    trimmed.toDoubleOrNull()?.let { return JsonPrimitive(it) }
    return when (trimmed.lowercase()) {
        "true" -> JsonPrimitive(true)
        "false" -> JsonPrimitive(false)
        else -> JsonPrimitive(raw)
    }
}

// Drop a trailing ".0" so a whole-number knob like a 2.0× multiplier shows as "2" in the field.
private fun formatDecimal(value: Double): String =
    if (value == value.toLong().toDouble()) value.toLong().toString() else value.toString()

@Composable
private fun Badge(
    label: String,
    background: Color,
    foreground: Color,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(background)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(text = label, style = typography.xs, color = foreground, maxLines = 1)
    }
}


@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.games_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.games_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
