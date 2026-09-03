// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.runtime.toMutableStateList
import androidx.compose.material3.Text
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.RadioGroup
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TRUST_POLICY_DEFAULTS
import bot.nomnomz.dashboard.core.network.TrustPolicy
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings
import bot.nomnomz.dashboard.core.network.UpdateTrustPolicyBody
import bot.nomnomz.dashboard.core.network.UpdateTwitchAutoModSettingsBody
import bot.nomnomz.dashboard.core.network.asUpdateBody
import bot.nomnomz.dashboard.feature.moderation.state.AutoBanSource
import bot.nomnomz.dashboard.feature.moderation.state.AutomationLine
import bot.nomnomz.dashboard.feature.moderation.state.AutomodFilter
import bot.nomnomz.dashboard.feature.moderation.state.trustWeightsAreValid
import bot.nomnomz.dashboard.feature.moderation.state.twitchCategoryLevels
import kotlin.math.round
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_automation_auto_ban_from
import nomnomzbot.composeapp.generated.resources.moderation_automation_auto_ban_none
import nomnomzbot.composeapp.generated.resources.moderation_automation_auto_ban_source_filter
import nomnomzbot.composeapp.generated.resources.moderation_automation_auto_ban_source_ladder
import nomnomzbot.composeapp.generated.resources.moderation_automation_auto_ban_source_rule
import nomnomzbot.composeapp.generated.resources.moderation_automation_can_ban_note
import nomnomzbot.composeapp.generated.resources.moderation_automation_custom_filters_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_custom_filters_on
import nomnomzbot.composeapp.generated.resources.moderation_automation_escalation_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_escalation_step
import nomnomzbot.composeapp.generated.resources.moderation_automation_escalation_step_timeout
import nomnomzbot.composeapp.generated.resources.moderation_automation_filter_caps
import nomnomzbot.composeapp.generated.resources.moderation_automation_filter_emotes
import nomnomzbot.composeapp.generated.resources.moderation_automation_filter_link
import nomnomzbot.composeapp.generated.resources.moderation_automation_filter_phrases
import nomnomzbot.composeapp.generated.resources.moderation_automation_filters_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_heat_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_heat_on
import nomnomzbot.composeapp.generated.resources.moderation_automation_panel_explain
import nomnomzbot.composeapp.generated.resources.moderation_automation_panel_title
import nomnomzbot.composeapp.generated.resources.moderation_automation_rules_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_rules_on
import nomnomzbot.composeapp.generated.resources.moderation_automation_twitch_categories
import nomnomzbot.composeapp.generated.resources.moderation_automation_twitch_off
import nomnomzbot.composeapp.generated.resources.moderation_automation_twitch_overall
import nomnomzbot.composeapp.generated.resources.moderation_automation_twitch_unavailable
import nomnomzbot.composeapp.generated.resources.moderation_trust_ban_penalty_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_ban_penalty_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_decay_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_decay_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_default_value
import nomnomzbot.composeapp.generated.resources.moderation_trust_group_decays
import nomnomzbot.composeapp.generated.resources.moderation_trust_group_heat
import nomnomzbot.composeapp.generated.resources.moderation_trust_group_penalties
import nomnomzbot.composeapp.generated.resources.moderation_trust_group_tiers
import nomnomzbot.composeapp.generated.resources.moderation_trust_group_weights
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_action_automod_denied
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_action_ban
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_action_filter_hit
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_action_report_validated
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_action_timeout
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_delta_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_delta_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_half_life_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_heat_half_life_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_invalid_number
import nomnomzbot.composeapp.generated.resources.moderation_trust_measure_account_age
import nomnomzbot.composeapp.generated.resources.moderation_trust_measure_content_age
import nomnomzbot.composeapp.generated.resources.moderation_trust_measure_content_popularity
import nomnomzbot.composeapp.generated.resources.moderation_trust_measure_request_count
import nomnomzbot.composeapp.generated.resources.moderation_trust_not_following_factor_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_not_following_factor_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_pinned
import nomnomzbot.composeapp.generated.resources.moderation_trust_reputation_boost_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_reputation_boost_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_reset
import nomnomzbot.composeapp.generated.resources.moderation_trust_save
import nomnomzbot.composeapp.generated.resources.moderation_trust_section_blast_radius
import nomnomzbot.composeapp.generated.resources.moderation_trust_section_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_skip_penalty_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_skip_penalty_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_low_max_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_low_max_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_standard_max_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_standard_max_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_untrusted_max_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_tier_untrusted_max_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_timeout_penalty_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_timeout_penalty_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_using_defaults
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_account_age_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_account_age_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_content_age_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_content_age_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_content_popularity_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_content_popularity_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_request_count_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_request_count_title
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_sum
import nomnomzbot.composeapp.generated.resources.moderation_trust_weight_sum_error
import nomnomzbot.composeapp.generated.resources.moderation_trust_youtube_quality_explain
import nomnomzbot.composeapp.generated.resources.moderation_trust_youtube_quality_title
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_aggression
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_bullying
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_disability
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_misogyny
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_race
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_sex_based_terms
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_sexuality
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_category_swearing
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_explain
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_level_label
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_level_range_error
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_mode_categories
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_mode_explain
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_mode_overall
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_save
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_title
import nomnomzbot.composeapp.generated.resources.moderation_twitch_automod_unavailable
import org.jetbrains.compose.resources.stringResource

// The Moderation screen's "Trust & Automation" section (S-OWN23 T4), in three parts:
//
//   1. AutomationPanel      — a DERIVED, truthful account of what the bot does without asking a human.
//   2. TrustPolicyEditor    — every trust-policy number, each with its value, its shipped default, a
//                             plain-language explanation and a per-field reset; the four weights show their live
//                             sum and block a save the backend would reject.
//   3. TwitchAutoModEditor  — Twitch's own AutoMod levels, in the overall OR per-category shape Twitch accepts.
//
// It lives beside ModerationScreen.kt rather than inside it: the screen file is already the page's assembly, and
// this section is one self-contained responsibility with its own field table.

/** Number of Twitch AutoMod levels: 0 (off) through 4 (strictest). */
private const val TWITCH_AUTOMOD_MAX_LEVEL: Int = 4

/** How many decimals a trust value keeps when rendered (enough for the 0.0003 popularity decay). */
private const val TRUST_VALUE_SCALE: Long = 1_000_000L

/**
 * Render [value] as a plain decimal — never scientific notation (Kotlin/Wasm renders 0.0003 as `3.0E-4`, which
 * is unreadable in an editable field and un-typeable back). Trailing zeros are trimmed to one decimal minimum.
 */
internal fun formatTrustValue(value: Double): String {
    val negative: Boolean = value < 0.0
    val magnitude: Double = if (negative) -value else value
    val scaled: Long = round(magnitude * TRUST_VALUE_SCALE).toLong()
    val whole: Long = scaled / TRUST_VALUE_SCALE
    val fraction: String = (scaled % TRUST_VALUE_SCALE).toString().padStart(6, '0').trimEnd('0')
    val sign: String = if (negative) "-" else ""
    return sign + whole.toString() + "." + fraction.ifEmpty { "0" }
}

/** One editable trust-policy number: what it is, what it is now, what it ships as, and how to write it back. */
private class TrustNumberField(
    val labelText: String,
    val explain: String,
    val current: Double,
    val default: Double,
    val isWeight: Boolean,
    val apply: (TrustPolicy, Double) -> TrustPolicy,
)

/**
 * "What happens automatically" — renders the already-derived [lines]. This composable adds no claims of its own:
 * it maps each computed line to its localized sentence, in order, and closes with the auto-ban note.
 */
@Composable
internal fun AutomationPanel(lines: List<AutomationLine>) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.moderation_automation_panel_title),
            style = typography.lg,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.moderation_automation_panel_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        lines.forEach { line ->
            Text(text = automationLineText(line), style = typography.sm, color = tokens.cardForeground)
        }
        Text(
            text = stringResource(Res.string.moderation_automation_can_ban_note),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    }
}

/** The one localized sentence for a computed [line]. Every branch is data-driven — no line is asserted. */
@Composable
private fun automationLineText(line: AutomationLine): String =
    when (line) {
        is AutomationLine.TwitchAutoModUnavailable ->
            stringResource(Res.string.moderation_automation_twitch_unavailable)
        is AutomationLine.TwitchAutoModOverall ->
            stringResource(Res.string.moderation_automation_twitch_overall, line.level)
        is AutomationLine.TwitchAutoModPerCategory ->
            stringResource(
                Res.string.moderation_automation_twitch_categories,
                line.activeCategories,
                line.strictest,
            )
        is AutomationLine.TwitchAutoModOff -> stringResource(Res.string.moderation_automation_twitch_off)
        is AutomationLine.LocalFilterOn ->
            when (line.filter) {
                AutomodFilter.Link ->
                    stringResource(Res.string.moderation_automation_filter_link, line.detail ?: 0)
                AutomodFilter.Caps ->
                    stringResource(Res.string.moderation_automation_filter_caps, line.detail ?: 0)
                AutomodFilter.Phrases ->
                    stringResource(Res.string.moderation_automation_filter_phrases, line.detail ?: 0)
                AutomodFilter.Emotes ->
                    stringResource(Res.string.moderation_automation_filter_emotes, line.detail ?: 0)
            }
        is AutomationLine.LocalFiltersOff -> stringResource(Res.string.moderation_automation_filters_off)
        is AutomationLine.CustomFiltersOn ->
            stringResource(Res.string.moderation_automation_custom_filters_on, line.count, line.banning)
        is AutomationLine.CustomFiltersOff ->
            stringResource(Res.string.moderation_automation_custom_filters_off)
        is AutomationLine.RulesOn ->
            stringResource(Res.string.moderation_automation_rules_on, line.count, line.banning)
        is AutomationLine.RulesOff -> stringResource(Res.string.moderation_automation_rules_off)
        is AutomationLine.HeatAutoTimeoutOn ->
            stringResource(Res.string.moderation_automation_heat_on, line.threshold, line.seconds)
        is AutomationLine.HeatAutoTimeoutOff ->
            stringResource(Res.string.moderation_automation_heat_off, line.threshold)
        is AutomationLine.EscalationStep ->
            if (line.timeoutSeconds != null) {
                stringResource(
                    Res.string.moderation_automation_escalation_step_timeout,
                    line.atOffense,
                    line.timeoutSeconds,
                )
            } else {
                stringResource(
                    Res.string.moderation_automation_escalation_step,
                    line.atOffense,
                    line.action,
                )
            }
        is AutomationLine.EscalationOff -> stringResource(Res.string.moderation_automation_escalation_off)
        is AutomationLine.AutoBanFrom -> {
            // Resolved one at a time: joinToString takes a non-inline lambda, which cannot call a composable.
            val names: MutableList<String> = mutableListOf()
            line.sources.forEach { source -> names.add(autoBanSourceText(source)) }
            stringResource(
                Res.string.moderation_automation_auto_ban_from,
                names.joinToString(separator = ", "),
            )
        }
        is AutomationLine.AutoBanNone -> stringResource(Res.string.moderation_automation_auto_ban_none)
    }

/** The localized name of one configured path to an automatic ban. */
@Composable
private fun autoBanSourceText(source: AutoBanSource): String =
    when (source) {
        is AutoBanSource.EscalationLadder ->
            stringResource(Res.string.moderation_automation_auto_ban_source_ladder, source.atOffense)
        is AutoBanSource.CustomFilter ->
            stringResource(Res.string.moderation_automation_auto_ban_source_filter, source.name)
        is AutoBanSource.Rule ->
            stringResource(Res.string.moderation_automation_auto_ban_source_rule, source.name)
    }

/**
 * The trust-policy editor: every field with its current value, its shipped default and a per-field reset.
 *
 * The four score weights carry a live sum and an inline error when it is not 1.0 — the backend rejects such a
 * body, so Save is blocked here rather than letting the user discover it as a server error. [manage] is the
 * broadcaster-level decision; a caller below the floor sees every control disabled with the reason.
 */
@Composable
internal fun TrustPolicyEditor(
    policy: TrustPolicy,
    manage: ManageDecision,
    weightSumInvalid: Boolean,
    onSave: (UpdateTrustPolicyBody) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val fields: List<TrustNumberField> = trustNumberFields(policy)
    // Drafts are text, not numbers, so a half-typed "0." survives a recomposition. They re-seed whenever the
    // loaded policy changes (first load, and after a save echoes the stored row back).
    val drafts: SnapshotStateList<String> =
        remember(policy) { fields.map { field -> formatTrustValue(field.current) }.toMutableStateList() }
    var reputationBoost: Boolean by remember(policy) { mutableStateOf(policy.reputationBoostEnabled) }

    val parsed: List<Double?> = drafts.map { draft -> draft.trim().toDoubleOrNull() }
    val allParsed: Boolean = parsed.none { value -> value == null }
    val weightSum: Double =
        fields.indices.filter { index -> fields[index].isWeight }.sumOf { index -> parsed[index] ?: 0.0 }

    val edited: TrustPolicy =
        fields
            .foldIndexed(policy) { index, acc, field ->
                val value: Double? = parsed[index]
                if (value == null) acc else field.apply(acc, value)
            }
            .copy(reputationBoostEnabled = reputationBoost)
    val body: UpdateTrustPolicyBody = edited.asUpdateBody()
    val sumValid: Boolean = allParsed && trustWeightsAreValid(body)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.moderation_trust_section_blast_radius),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text =
                stringResource(
                    if (policy.isPinned) Res.string.moderation_trust_pinned
                    else Res.string.moderation_trust_using_defaults
                ),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        TrustGroupHeader(stringResource(Res.string.moderation_trust_group_weights))
        RenderTrustFields(fields, drafts, manage, from = 0, until = WEIGHT_FIELD_COUNT)
        Text(
            text = stringResource(Res.string.moderation_trust_weight_sum, formatTrustValue(weightSum)),
            style = typography.sm,
            color = if (sumValid) tokens.mutedForeground else tokens.destructive,
        )
        if (!sumValid || weightSumInvalid) {
            Text(
                text = stringResource(Res.string.moderation_trust_weight_sum_error),
                style = typography.sm,
                color = tokens.destructive,
            )
        }

        Separator()
        TrustGroupHeader(stringResource(Res.string.moderation_trust_group_decays))
        RenderTrustFields(fields, drafts, manage, from = WEIGHT_FIELD_COUNT, until = DECAY_FIELDS_END)

        Separator()
        TrustGroupHeader(stringResource(Res.string.moderation_trust_group_penalties))
        RenderTrustFields(fields, drafts, manage, from = DECAY_FIELDS_END, until = PENALTY_FIELDS_END)
        // The reputation boost is the policy's one switch: mods, VIPs, subs and regulars start ahead.
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = stringResource(Res.string.moderation_trust_reputation_boost_title),
                    style = typography.base,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.moderation_trust_reputation_boost_explain),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Text(
                    text =
                        stringResource(
                            Res.string.moderation_trust_default_value,
                            TRUST_POLICY_DEFAULTS.reputationBoostEnabled.toString(),
                        ),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
            ManageGate(decision = manage) { enabled ->
                Switch(
                    checked = reputationBoost,
                    onCheckedChange = { checked -> reputationBoost = checked },
                    enabled = enabled,
                )
            }
        }

        Separator()
        TrustGroupHeader(stringResource(Res.string.moderation_trust_group_tiers))
        RenderTrustFields(fields, drafts, manage, from = PENALTY_FIELDS_END, until = TIER_FIELDS_END)

        Separator()
        TrustGroupHeader(stringResource(Res.string.moderation_trust_group_heat))
        RenderTrustFields(fields, drafts, manage, from = TIER_FIELDS_END, until = fields.size)

        ManageGate(decision = manage) { enabled ->
            Button(onClick = { onSave(body) }, enabled = enabled && allParsed && sumValid) {
                Text(stringResource(Res.string.moderation_trust_save))
            }
        }
    }
}

/** A group heading inside the trust editor (weights / growth speeds / penalties / tiers / heat). */
@Composable
private fun TrustGroupHeader(labelText: String) {
    Text(text = labelText, style = LocalTypography.current.base, color = LocalTokens.current.cardForeground)
}

/** Render the field rows in `[from, until)` of the field table against their text [drafts]. */
@Composable
private fun RenderTrustFields(
    fields: List<TrustNumberField>,
    drafts: SnapshotStateList<String>,
    manage: ManageDecision,
    from: Int,
    until: Int,
) {
    val invalidNumber: String = stringResource(Res.string.moderation_trust_invalid_number)
    val resetLabel: String = stringResource(Res.string.moderation_trust_reset)
    for (index in from until until) {
        val field: TrustNumberField = fields[index]
        val draft: String = drafts[index]
        val valid: Boolean = draft.trim().toDoubleOrNull() != null
        val defaultLine: String =
            stringResource(Res.string.moderation_trust_default_value, formatTrustValue(field.default))
        ManageGate(decision = manage) { enabled ->
            AppTextField(
                value = draft,
                onValueChange = { typed -> drafts[index] = typed },
                label = field.labelText,
                enabled = enabled,
                isError = !valid,
                errorText = if (valid) null else invalidNumber,
                supportingText = field.explain + " " + defaultLine,
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                actionLabel = resetLabel,
                onActionClick =
                    if (enabled) {
                        { drafts[index] = formatTrustValue(field.default) }
                    } else {
                        null
                    },
                modifier = Modifier.fillMaxWidth(),
            )
        }
    }
}

/** How many of the leading fields are score weights (the ones that must sum to 1.0). */
private const val WEIGHT_FIELD_COUNT: Int = 4

/** Index just past the four growth-speed (decay) fields. */
private const val DECAY_FIELDS_END: Int = 8

/** Index just past the not-following / YouTube-quality / skip / timeout / ban penalties. */
private const val PENALTY_FIELDS_END: Int = 13

/** Index just past the three tier ceilings. */
private const val TIER_FIELDS_END: Int = 16

/**
 * The whole editable trust policy as an ordered table: weights, growth speeds, penalties, tier ceilings, heat.
 * The order is what the group index constants above slice, and what the editor renders.
 */
@Composable
private fun trustNumberFields(policy: TrustPolicy): List<TrustNumberField> {
    val decayExplain: String = stringResource(Res.string.moderation_trust_decay_explain)
    val heatDeltaExplain: String = stringResource(Res.string.moderation_trust_heat_delta_explain)
    return listOf(
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_weight_request_count_title),
            explain = stringResource(Res.string.moderation_trust_weight_request_count_explain),
            current = policy.requestCountWeight,
            default = TRUST_POLICY_DEFAULTS.requestCountWeight,
            isWeight = true,
            apply = { p, v -> p.copy(requestCountWeight = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_weight_account_age_title),
            explain = stringResource(Res.string.moderation_trust_weight_account_age_explain),
            current = policy.accountAgeWeight,
            default = TRUST_POLICY_DEFAULTS.accountAgeWeight,
            isWeight = true,
            apply = { p, v -> p.copy(accountAgeWeight = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_weight_content_age_title),
            explain = stringResource(Res.string.moderation_trust_weight_content_age_explain),
            current = policy.contentAgeWeight,
            default = TRUST_POLICY_DEFAULTS.contentAgeWeight,
            isWeight = true,
            apply = { p, v -> p.copy(contentAgeWeight = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_weight_content_popularity_title),
            explain = stringResource(Res.string.moderation_trust_weight_content_popularity_explain),
            current = policy.contentPopularityWeight,
            default = TRUST_POLICY_DEFAULTS.contentPopularityWeight,
            isWeight = true,
            apply = { p, v -> p.copy(contentPopularityWeight = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_decay_title,
                    stringResource(Res.string.moderation_trust_measure_request_count),
                ),
            explain = decayExplain,
            current = policy.requestCountDecay,
            default = TRUST_POLICY_DEFAULTS.requestCountDecay,
            isWeight = false,
            apply = { p, v -> p.copy(requestCountDecay = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_decay_title,
                    stringResource(Res.string.moderation_trust_measure_account_age),
                ),
            explain = decayExplain,
            current = policy.accountAgeDecay,
            default = TRUST_POLICY_DEFAULTS.accountAgeDecay,
            isWeight = false,
            apply = { p, v -> p.copy(accountAgeDecay = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_decay_title,
                    stringResource(Res.string.moderation_trust_measure_content_age),
                ),
            explain = decayExplain,
            current = policy.contentAgeDecay,
            default = TRUST_POLICY_DEFAULTS.contentAgeDecay,
            isWeight = false,
            apply = { p, v -> p.copy(contentAgeDecay = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_decay_title,
                    stringResource(Res.string.moderation_trust_measure_content_popularity),
                ),
            explain = decayExplain,
            current = policy.contentPopularityDecay,
            default = TRUST_POLICY_DEFAULTS.contentPopularityDecay,
            isWeight = false,
            apply = { p, v -> p.copy(contentPopularityDecay = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_not_following_factor_title),
            explain = stringResource(Res.string.moderation_trust_not_following_factor_explain),
            current = policy.notFollowingFactor,
            default = TRUST_POLICY_DEFAULTS.notFollowingFactor,
            isWeight = false,
            apply = { p, v -> p.copy(notFollowingFactor = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_youtube_quality_title),
            explain = stringResource(Res.string.moderation_trust_youtube_quality_explain),
            current = policy.youTubeQualityPenaltyFactor,
            default = TRUST_POLICY_DEFAULTS.youTubeQualityPenaltyFactor,
            isWeight = false,
            apply = { p, v -> p.copy(youTubeQualityPenaltyFactor = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_skip_penalty_title),
            explain = stringResource(Res.string.moderation_trust_skip_penalty_explain),
            current = policy.skipPenalty,
            default = TRUST_POLICY_DEFAULTS.skipPenalty,
            isWeight = false,
            apply = { p, v -> p.copy(skipPenalty = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_timeout_penalty_title),
            explain = stringResource(Res.string.moderation_trust_timeout_penalty_explain),
            current = policy.timeoutPenalty,
            default = TRUST_POLICY_DEFAULTS.timeoutPenalty,
            isWeight = false,
            apply = { p, v -> p.copy(timeoutPenalty = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_ban_penalty_title),
            explain = stringResource(Res.string.moderation_trust_ban_penalty_explain),
            current = policy.banPenalty,
            default = TRUST_POLICY_DEFAULTS.banPenalty,
            isWeight = false,
            apply = { p, v -> p.copy(banPenalty = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_tier_untrusted_max_title),
            explain = stringResource(Res.string.moderation_trust_tier_untrusted_max_explain),
            current = policy.untrustedMax,
            default = TRUST_POLICY_DEFAULTS.untrustedMax,
            isWeight = false,
            apply = { p, v -> p.copy(untrustedMax = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_tier_low_max_title),
            explain = stringResource(Res.string.moderation_trust_tier_low_max_explain),
            current = policy.lowMax,
            default = TRUST_POLICY_DEFAULTS.lowMax,
            isWeight = false,
            apply = { p, v -> p.copy(lowMax = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_tier_standard_max_title),
            explain = stringResource(Res.string.moderation_trust_tier_standard_max_explain),
            current = policy.standardMax,
            default = TRUST_POLICY_DEFAULTS.standardMax,
            isWeight = false,
            apply = { p, v -> p.copy(standardMax = v) },
        ),
        TrustNumberField(
            labelText = stringResource(Res.string.moderation_trust_heat_half_life_title),
            explain = stringResource(Res.string.moderation_trust_heat_half_life_explain),
            current = policy.heatHalfLifeHours,
            default = TRUST_POLICY_DEFAULTS.heatHalfLifeHours,
            isWeight = false,
            apply = { p, v -> p.copy(heatHalfLifeHours = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_heat_delta_title,
                    stringResource(Res.string.moderation_trust_heat_action_ban),
                ),
            explain = heatDeltaExplain,
            current = policy.heatDeltaBan,
            default = TRUST_POLICY_DEFAULTS.heatDeltaBan,
            isWeight = false,
            apply = { p, v -> p.copy(heatDeltaBan = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_heat_delta_title,
                    stringResource(Res.string.moderation_trust_heat_action_timeout),
                ),
            explain = heatDeltaExplain,
            current = policy.heatDeltaTimeout,
            default = TRUST_POLICY_DEFAULTS.heatDeltaTimeout,
            isWeight = false,
            apply = { p, v -> p.copy(heatDeltaTimeout = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_heat_delta_title,
                    stringResource(Res.string.moderation_trust_heat_action_report_validated),
                ),
            explain = heatDeltaExplain,
            current = policy.heatDeltaReportValidated,
            default = TRUST_POLICY_DEFAULTS.heatDeltaReportValidated,
            isWeight = false,
            apply = { p, v -> p.copy(heatDeltaReportValidated = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_heat_delta_title,
                    stringResource(Res.string.moderation_trust_heat_action_automod_denied),
                ),
            explain = heatDeltaExplain,
            current = policy.heatDeltaAutoModDenied,
            default = TRUST_POLICY_DEFAULTS.heatDeltaAutoModDenied,
            isWeight = false,
            apply = { p, v -> p.copy(heatDeltaAutoModDenied = v) },
        ),
        TrustNumberField(
            labelText =
                stringResource(
                    Res.string.moderation_trust_heat_delta_title,
                    stringResource(Res.string.moderation_trust_heat_action_filter_hit),
                ),
            explain = heatDeltaExplain,
            current = policy.heatDeltaFilterHit,
            default = TRUST_POLICY_DEFAULTS.heatDeltaFilterHit,
            isWeight = false,
            apply = { p, v -> p.copy(heatDeltaFilterHit = v) },
        ),
    )
}

/** The two shapes Twitch accepts — one overall dial, or the eight per-category levels. Never both. */
private enum class TwitchAutoModMode {
    Overall,
    PerCategory,
}

/**
 * Twitch's own AutoMod levels. [settings] is null when the live read failed — the form then says so instead of
 * offering an edit against unknown state. The mode radio decides which shape is sent, and the send goes through
 * the [UpdateTwitchAutoModSettingsBody] factories, so an overall level and categories can never travel together.
 */
@Composable
internal fun TwitchAutoModEditor(
    settings: TwitchAutoModSettings?,
    manage: ManageDecision,
    onSave: (UpdateTwitchAutoModSettingsBody) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    if (settings == null) {
        Text(
            text = stringResource(Res.string.moderation_twitch_automod_unavailable),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        )
        return
    }

    val categoryTitles: List<String> =
        listOf(
            stringResource(Res.string.moderation_twitch_automod_category_aggression),
            stringResource(Res.string.moderation_twitch_automod_category_bullying),
            stringResource(Res.string.moderation_twitch_automod_category_disability),
            stringResource(Res.string.moderation_twitch_automod_category_misogyny),
            stringResource(Res.string.moderation_twitch_automod_category_race),
            stringResource(Res.string.moderation_twitch_automod_category_sex_based_terms),
            stringResource(Res.string.moderation_twitch_automod_category_sexuality),
            stringResource(Res.string.moderation_twitch_automod_category_swearing),
        )

    var mode: TwitchAutoModMode by
        remember(settings) {
            mutableStateOf(
                if (settings.overallLevel != null) TwitchAutoModMode.Overall else TwitchAutoModMode.PerCategory
            )
        }
    var overallDraft: String by remember(settings) { mutableStateOf((settings.overallLevel ?: 0).toString()) }
    val categoryDrafts: SnapshotStateList<String> =
        remember(settings) { twitchCategoryLevels(settings).map { it.toString() }.toMutableStateList() }

    val overallLevel: Int? = overallDraft.trim().toIntOrNull()?.takeIf { it in 0..TWITCH_AUTOMOD_MAX_LEVEL }
    val categoryLevels: List<Int?> =
        categoryDrafts.map { draft -> draft.trim().toIntOrNull()?.takeIf { it in 0..TWITCH_AUTOMOD_MAX_LEVEL } }
    val valid: Boolean =
        when (mode) {
            TwitchAutoModMode.Overall -> overallLevel != null
            TwitchAutoModMode.PerCategory -> categoryLevels.none { level -> level == null }
        }
    val rangeError: String = stringResource(Res.string.moderation_twitch_automod_level_range_error)
    val modeLabels: Map<TwitchAutoModMode, String> =
        mapOf(
            TwitchAutoModMode.Overall to stringResource(Res.string.moderation_twitch_automod_mode_overall),
            TwitchAutoModMode.PerCategory to
                stringResource(Res.string.moderation_twitch_automod_mode_categories),
        )

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.moderation_twitch_automod_title),
            style = typography.lg,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.moderation_twitch_automod_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = stringResource(Res.string.moderation_twitch_automod_mode_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        ManageGate(decision = manage) { enabled ->
            RadioGroup(
                options = listOf(TwitchAutoModMode.Overall, TwitchAutoModMode.PerCategory),
                selected = mode,
                onSelectedChange = { chosen -> mode = chosen },
                label = { option -> modeLabels.getValue(option) },
                enabled = { enabled },
            )
        }

        when (mode) {
            TwitchAutoModMode.Overall ->
                ManageGate(decision = manage) { enabled ->
                    AppTextField(
                        value = overallDraft,
                        onValueChange = { typed -> overallDraft = typed.filter { ch -> ch.isDigit() } },
                        label = stringResource(Res.string.moderation_twitch_automod_level_label),
                        enabled = enabled,
                        isError = overallLevel == null,
                        errorText = if (overallLevel == null) rangeError else null,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            TwitchAutoModMode.PerCategory ->
                categoryTitles.forEachIndexed { index, labelText ->
                    val level: Int? = categoryLevels[index]
                    ManageGate(decision = manage) { enabled ->
                        AppTextField(
                            value = categoryDrafts[index],
                            onValueChange = { typed ->
                                categoryDrafts[index] = typed.filter { ch -> ch.isDigit() }
                            },
                            label = labelText,
                            enabled = enabled,
                            isError = level == null,
                            errorText = if (level == null) rangeError else null,
                            supportingText = stringResource(Res.string.moderation_twitch_automod_level_label),
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
        }

        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    // Exactly one shape leaves this screen: the factories are the only constructors, so the
                    // overall-plus-categories body the backend rejects cannot be built here.
                    val body: UpdateTwitchAutoModSettingsBody? =
                        when (mode) {
                            TwitchAutoModMode.Overall ->
                                overallLevel?.let { level -> UpdateTwitchAutoModSettingsBody.overall(level) }
                            TwitchAutoModMode.PerCategory ->
                                if (categoryLevels.none { it == null }) {
                                    UpdateTwitchAutoModSettingsBody.categories(
                                        aggression = categoryLevels[0] ?: 0,
                                        bullying = categoryLevels[1] ?: 0,
                                        disability = categoryLevels[2] ?: 0,
                                        misogyny = categoryLevels[3] ?: 0,
                                        raceEthnicityOrReligion = categoryLevels[4] ?: 0,
                                        sexBasedTerms = categoryLevels[5] ?: 0,
                                        sexualitySexOrGender = categoryLevels[6] ?: 0,
                                        swearing = categoryLevels[7] ?: 0,
                                    )
                                } else {
                                    null
                                }
                        }
                    body?.let(onSave)
                },
                enabled = enabled && valid,
            ) {
                Text(stringResource(Res.string.moderation_twitch_automod_save))
            }
        }
    }
}

/** The section heading the Moderation screen renders above these three cards. */
@Composable
internal fun trustAutomationSectionTitle(): String = stringResource(Res.string.moderation_trust_section_title)
