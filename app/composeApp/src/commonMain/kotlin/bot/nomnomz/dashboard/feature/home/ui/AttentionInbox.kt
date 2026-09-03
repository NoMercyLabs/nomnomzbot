// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.CloseGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.time.Elapsed
import bot.nomnomz.dashboard.core.time.RelativeTime
import bot.nomnomz.dashboard.feature.home.state.AttentionSeverity
import bot.nomnomz.dashboard.feature.home.state.HeldReviewState
import bot.nomnomz.dashboard.feature.home.state.attentionSeverityFor
import bot.nomnomz.dashboard.feature.moderation.ui.TrustHeatBadges
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionForAction
import kotlinx.datetime.Clock
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.home_action_required_section
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_critical
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_info
import nomnomzbot.composeapp.generated.resources.home_action_required_severity_warning
import nomnomzbot.composeapp.generated.resources.home_attention_count_badge
import nomnomzbot.composeapp.generated.resources.home_attention_detected_ago
import nomnomzbot.composeapp.generated.resources.home_attention_dismiss
import nomnomzbot.composeapp.generated.resources.home_attention_review
import nomnomzbot.composeapp.generated.resources.home_held_allow
import nomnomzbot.composeapp.generated.resources.home_held_ban
import nomnomzbot.composeapp.generated.resources.home_held_ban_reason_label
import nomnomzbot.composeapp.generated.resources.home_held_block
import nomnomzbot.composeapp.generated.resources.home_held_block_term
import nomnomzbot.composeapp.generated.resources.home_held_bulk_title
import nomnomzbot.composeapp.generated.resources.home_held_close
import nomnomzbot.composeapp.generated.resources.home_held_modal_held_just_now
import nomnomzbot.composeapp.generated.resources.home_held_modal_held_minutes
import nomnomzbot.composeapp.generated.resources.home_held_modal_held_hours
import nomnomzbot.composeapp.generated.resources.home_held_modal_held_days
import nomnomzbot.composeapp.generated.resources.home_held_modal_repeated
import nomnomzbot.composeapp.generated.resources.home_held_modal_category
import nomnomzbot.composeapp.generated.resources.home_held_modal_empty
import nomnomzbot.composeapp.generated.resources.home_held_modal_error
import nomnomzbot.composeapp.generated.resources.home_held_modal_loading
import nomnomzbot.composeapp.generated.resources.home_held_modal_title
import nomnomzbot.composeapp.generated.resources.home_held_term_blocked
import nomnomzbot.composeapp.generated.resources.home_held_timeout
import nomnomzbot.composeapp.generated.resources.home_held_timeout_preset_10m
import nomnomzbot.composeapp.generated.resources.home_held_timeout_preset_1d
import nomnomzbot.composeapp.generated.resources.home_held_timeout_preset_1h
import nomnomzbot.composeapp.generated.resources.home_held_timeout_preset_60s
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The actionable attention inbox (S-OWN22 Task 4) — replaces the old ActionRequiredCard/ActionRequiredRow.
// Every row is a real, already-detected condition (held AutoMod messages grouped per user, dead integration
// tokens) with real actions: Review (held → the review dialog; token → the Integrations page) and a persisted
// Dismiss. Severity renders three-way (critical/warning/info) — the old card binarised everything non-critical
// into "Warning". Rendered only when [items] is non-empty; the caller skips the whole card on an empty list.
@Composable
fun AttentionInbox(
    items: List<ActionRequiredItem>,
    attentionError: String?,
    onReview: (ActionRequiredItem) -> Unit,
    onDismiss: (ActionRequiredItem) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.home_action_required_section),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            attentionError?.let { error -> ActionErrorBanner(message = error) }
            items.forEach { item ->
                AttentionRow(item = item, onReview = { onReview(item) }, onDismiss = { onDismiss(item) })
            }
        }
    }
}

@Composable
private fun AttentionRow(
    item: ActionRequiredItem,
    onReview: () -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val severity: AttentionSeverity = attentionSeverityFor(item.severity)
    val severityVariant: BadgeVariant =
        when (severity) {
            AttentionSeverity.Critical -> BadgeVariant.Destructive
            AttentionSeverity.Warning -> BadgeVariant.Default
            AttentionSeverity.Info -> BadgeVariant.Secondary
        }
    val severityLabel: StringResource =
        when (severity) {
            AttentionSeverity.Critical -> Res.string.home_action_required_severity_critical
            AttentionSeverity.Warning -> Res.string.home_action_required_severity_warning
            AttentionSeverity.Info -> Res.string.home_action_required_severity_info
        }
    val now = remember { Clock.System.now() }

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .clickable(onClick = onReview)
            .padding(vertical = spacing.s2, horizontal = spacing.s1),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Badge(variant = severityVariant) {
            Text(text = stringResource(severityLabel).uppercase(), style = typography.xs)
        }
        Column(modifier = Modifier.weight(1f)) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    // Routed through resolveRowLabel (RowLabelGuardTest): a row whose backend title ever
                    // comes back blank must still render an actionable name, never an empty label.
                    text = resolveRowLabel(
                        primary = item.title,
                        secondary = item.message,
                        typeLabel = item.kind.ifBlank { "Notice" },
                        discriminatorSource = item.id,
                    ),
                    style = typography.sm,
                    fontWeight = FontWeight.SemiBold,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (item.count > 1) {
                    Badge(variant = BadgeVariant.Outline) {
                        Text(
                            text = stringResource(Res.string.home_attention_count_badge, item.count),
                            style = typography.xs,
                        )
                    }
                }
            }
            Text(
                text = item.message,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            RelativeTime.minutesSince(item.detectedAt, now)?.let { minutesAgo ->
                Text(
                    text = stringResource(Res.string.home_attention_detected_ago, minutesAgo.coerceAtLeast(0).toInt()),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        TextButton(onClick = onReview) {
            Text(text = stringResource(Res.string.home_attention_review))
        }
        GlyphButton(
            icon = CloseGlyph,
            label = stringResource(Res.string.home_attention_dismiss),
            onClick = onDismiss,
        )
    }
}

// ─── Held-message review dialog ───────────────────────────────────────────────

// The Twitch timeout presets the deny+timeout follow-up offers (60s / 10m / 1h / 1d).
private val TIMEOUT_PRESETS: List<Pair<StringResource, Int>> =
    listOf(
        Res.string.home_held_timeout_preset_60s to 60,
        Res.string.home_held_timeout_preset_10m to 600,
        Res.string.home_held_timeout_preset_1h to 3_600,
        Res.string.home_held_timeout_preset_1d to 86_400,
    )

/**
 * The held-message review dialog (S-OWN22 Task 4) — mirrors [bot.nomnomz.dashboard.feature.moderation.ui]'s
 * per-user context dialog patterns and REUSES its [TrustHeatBadges]. Shows every pending held message of the
 * opened inbox item (full content snapshot, AutoMod category, held-at) with real actions per message AND in
 * bulk: Allow (approve) · Block (deny) · Timeout (deny + preset duration) · Ban (deny + optional reason), plus
 * Block term. All writes are backed by the caller's [heldActionKeys] (`moderation:queue:resolve`,
 * `moderation:blocklist:write`) — below the floor the buttons disable with the standard reason, never hide.
 */
@Composable
fun HeldReviewDialog(
    state: HeldReviewState,
    heldActionKeys: Set<String>,
    onResolve: (queueItemId: String, action: String, followUp: String?, timeoutSeconds: Int?, reason: String?) -> Unit,
    onResolveAll: (action: String, followUp: String?, timeoutSeconds: Int?, reason: String?) -> Unit,
    onBlockTerm: (term: String) -> Unit,
    onClose: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val resolveManage: ManageDecision =
        rememberManageDecisionForAction(heldActionKeys, "moderation:queue:resolve")
    val blocklistManage: ManageDecision =
        rememberManageDecisionForAction(heldActionKeys, "moderation:blocklist:write")

    AlertDialog(
        onDismissRequest = onClose,
        title = {
            Text(
                text = stringResource(Res.string.home_held_modal_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                when (state) {
                    is HeldReviewState.Loading ->
                        Text(
                            text = stringResource(Res.string.home_held_modal_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is HeldReviewState.Error ->
                        Text(
                            text = stringResource(Res.string.home_held_modal_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is HeldReviewState.Ready ->
                        HeldReviewBody(
                            state = state,
                            resolveManage = resolveManage,
                            blocklistManage = blocklistManage,
                            onResolve = onResolve,
                            onResolveAll = onResolveAll,
                            onBlockTerm = onBlockTerm,
                        )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onClose) {
                Text(stringResource(Res.string.home_held_close))
            }
        },
    )
}

@Composable
private fun HeldReviewBody(
    state: HeldReviewState.Ready,
    resolveManage: ManageDecision,
    blocklistManage: ManageDecision,
    onResolve: (queueItemId: String, action: String, followUp: String?, timeoutSeconds: Int?, reason: String?) -> Unit,
    onResolveAll: (action: String, followUp: String?, timeoutSeconds: Int?, reason: String?) -> Unit,
    onBlockTerm: (term: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // One shared optional ban reason for this dialog's ban actions (per-message and bulk alike).
    var banReason: String by remember { mutableStateOf("") }

    // The user context strip the Moderation screen's per-user panel already renders — the SAME badges (reuse).
    val chatterName: String =
        state.item.sourceUserName?.takeIf { it.isNotBlank() }
            ?: state.item.sourceUserId
            ?: ""
    if (chatterName.isNotBlank()) {
        Text(
            text = chatterName,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
    state.userContext?.trust?.let { trust ->
        TrustHeatBadges(trust = trust, heatThreshold = state.heatThreshold)
    }

    state.actionError?.let { error -> ActionErrorBanner(message = error) }
    state.blockedTerm?.let { term ->
        Text(
            text = stringResource(Res.string.home_held_term_blocked, term),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    }

    AppTextField(
        value = banReason,
        onValueChange = { banReason = it },
        label = stringResource(Res.string.home_held_ban_reason_label),
        modifier = Modifier.fillMaxWidth(),
    )

    if (state.messages.isEmpty()) {
        Text(
            text = stringResource(Res.string.home_held_modal_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    // Bulk row — every remaining message from this user in one go. Only meaningful for a group.
    if (state.messages.size > 1) {
        Text(
            text = stringResource(Res.string.home_held_bulk_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        HeldActionRow(
            resolveManage = resolveManage,
            onAllow = { onResolveAll("approve", null, null, null) },
            onBlock = { onResolveAll("deny", null, null, null) },
            onTimeout = { seconds -> onResolveAll("deny", "timeout", seconds, banReason.ifBlank { null }) },
            onBan = { onResolveAll("deny", "ban", null, banReason.ifBlank { null }) },
        )
        Separator()
    }

    // A spam wave is the SAME line posted over and over, so the raw list is six copies of one
    // message and six identical action rows to read past. One row per distinct text, carrying its
    // own repeat count, is the same information at a sixth of the reading cost — and one decision
    // then resolves every copy, which is what the moderator meant anyway.
    heldGroups(state.messages).forEachIndexed { index, group ->
        if (index > 0) Separator()
        HeldMessageGroupRow(
            group = group,
            resolveManage = resolveManage,
            blocklistManage = blocklistManage,
            onAllow = { group.ids.forEach { id -> onResolve(id, "approve", null, null, null) } },
            onBlock = { group.ids.forEach { id -> onResolve(id, "deny", null, null, null) } },
            onTimeout = { seconds ->
                group.ids.forEach { id ->
                    onResolve(id, "deny", "timeout", seconds, banReason.ifBlank { null })
                }
            },
            onBan = {
                group.ids.forEach { id -> onResolve(id, "deny", "ban", null, banReason.ifBlank { null }) }
            },
            onBlockTerm = onBlockTerm,
        )
    }
}

/**
 * One distinct held message plus every copy of it.
 *
 * [heldAt] is the OLDEST copy's timestamp — the age a moderator cares about is how long this has
 * been waiting, which is when it first arrived, not when it was last repeated.
 */
private data class HeldGroup(
    val text: String?,
    val category: String?,
    val heldAt: String?,
    val ids: List<String>,
)

/** Groups by exact message text, preserving the order the messages arrived in. */
private fun heldGroups(messages: List<ModerationQueueItem>): List<HeldGroup> =
    messages
        .groupBy { it.messageContentSnapshot?.takeIf { text -> text.isNotBlank() } ?: it.id }
        .map { (_, copies) ->
            HeldGroup(
                text = copies.first().messageContentSnapshot?.takeIf { it.isNotBlank() },
                category = copies.firstNotNullOfOrNull { it.autoModCategory?.takeIf { c -> c.isNotBlank() } },
                heldAt = copies.mapNotNull { it.createdAt }.minOrNull(),
                ids = copies.map { it.id },
            )
        }

// One held message and every copy of it: the content snapshot, its AutoMod category, how long it has
// been waiting, the repeat count, and the actions — all of which resolve the whole group.
@Composable
private fun HeldMessageGroupRow(
    group: HeldGroup,
    resolveManage: ManageDecision,
    blocklistManage: ManageDecision,
    onAllow: () -> Unit,
    onBlock: () -> Unit,
    onTimeout: (seconds: Int) -> Unit,
    onBan: () -> Unit,
    onBlockTerm: (term: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val now = remember { Clock.System.now() }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        group.text?.let { text ->
            Text(
                text = text,
                style = typography.sm,
                color = tokens.cardForeground,
            )
        }
        Row(
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (group.ids.size > 1) {
                Text(
                    text = stringResource(Res.string.home_held_modal_repeated, group.ids.size),
                    style = typography.xs,
                    color = tokens.cardForeground,
                )
            }
            group.category?.let { category ->
                Text(
                    text = stringResource(Res.string.home_held_modal_category, category),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            RelativeTime.elapsedSince(group.heldAt, now)?.let { elapsed ->
                Text(
                    text = heldAgoText(elapsed),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
        HeldActionRow(
            resolveManage = resolveManage,
            onAllow = onAllow,
            onBlock = onBlock,
            onTimeout = onTimeout,
            onBan = onBan,
            // Blocking the term belongs WITH the other decisions about this message, not floating on
            // its own line between rows where it reads as if it applied to the next one.
            trailing =
                group.text?.let { text ->
                    {
                        ManageGate(decision = blocklistManage) { enabled ->
                            TextButton(onClick = { onBlockTerm(text) }, enabled = enabled) {
                                Text(
                                    text = stringResource(Res.string.home_held_block_term),
                                    color = tokens.mutedForeground,
                                )
                            }
                        }
                    }
                },
        )
    }
}

/** The translated wording for a bucketed age — the unit choice is made in [RelativeTime]. */
@Composable
private fun heldAgoText(elapsed: Elapsed): String =
    when (elapsed) {
        is Elapsed.JustNow -> stringResource(Res.string.home_held_modal_held_just_now)
        is Elapsed.Minutes -> stringResource(Res.string.home_held_modal_held_minutes, elapsed.value)
        is Elapsed.Hours -> stringResource(Res.string.home_held_modal_held_hours, elapsed.value)
        is Elapsed.Days -> stringResource(Res.string.home_held_modal_held_days, elapsed.value)
    }

// The four resolve actions. Timeout expands its preset row (60s/10m/1h/1d) instead of firing blind; every
// button rides the caller's `moderation:queue:resolve` decision — disabled with a reason below the floor.
@Composable
private fun HeldActionRow(
    resolveManage: ManageDecision,
    onAllow: () -> Unit,
    onBlock: () -> Unit,
    onTimeout: (seconds: Int) -> Unit,
    onBan: () -> Unit,
    trailing: (@Composable () -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var showTimeoutPresets: Boolean by remember { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            ManageGate(decision = resolveManage) { enabled ->
                Button(onClick = onAllow, enabled = enabled, size = ButtonSize.Sm) {
                    Text(stringResource(Res.string.home_held_allow))
                }
            }
            ManageGate(decision = resolveManage) { enabled ->
                TextButton(onClick = onBlock, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.home_held_block),
                        color = tokens.mutedForeground,
                    )
                }
            }
            ManageGate(decision = resolveManage) { enabled ->
                TextButton(onClick = { showTimeoutPresets = !showTimeoutPresets }, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.home_held_timeout),
                        color = tokens.mutedForeground,
                    )
                }
            }
            ManageGate(decision = resolveManage) { enabled ->
                TextButton(onClick = onBan, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.home_held_ban),
                        color = tokens.destructive,
                    )
                }
            }
            trailing?.let { slot ->
                Spacer(modifier = Modifier.weight(1f))
                slot()
            }
        }
        if (showTimeoutPresets) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                TIMEOUT_PRESETS.forEach { (label, seconds) ->
                    ManageGate(decision = resolveManage) { enabled ->
                        TextButton(
                            onClick = {
                                showTimeoutPresets = false
                                onTimeout(seconds)
                            },
                            enabled = enabled,
                        ) {
                            Text(text = stringResource(label), color = tokens.mutedForeground)
                        }
                    }
                }
            }
        }
    }
}
