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
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.SpamDefensePolicy
import bot.nomnomz.dashboard.core.network.SpamDefenseSettings
import bot.nomnomz.dashboard.core.network.SpamSettingDescriptor
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_trust_save
import org.jetbrains.compose.resources.stringResource

/**
 * The spam-defence editor.
 *
 * It renders from the CATALOGUE the backend sends rather than from a hand-built form. That is what
 * keeps the owner's requirement true over time: a weight added to the engine arrives here with its
 * label, its explanation, its note on what moving it costs, and its bounds, and appears in the right
 * section without anyone editing this file. A hand-built form is how a settings page quietly stops
 * describing the machine it configures.
 *
 * Every control shows what moving it costs, not just what it is. A number with a range but no stated
 * consequence is a number nobody can tune honestly.
 */
@Composable
internal fun SpamDefenseSection(
    policy: SpamDefensePolicy,
    manage: ManageDecision,
    onSave: (SpamDefenseSettings) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var draft: SpamDefenseSettings by remember(policy) { mutableStateOf(policy.settings) }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The guarantees that have no switch, stated up front. An operator should be able to see what
        // they get for free rather than having to ask, and knowing the floor is what makes the rest of
        // the page safe to experiment with.
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            policy.invariants.forEach { invariant ->
                SpamDefenseCopy.resource(invariant.guaranteeKey)?.let { resource ->
                    Text(
                        text = stringResource(resource),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                }
            }
        }

        Separator()

        policy.catalogue
            .groupBy { it.group }
            .forEach { (group, descriptors) ->
                SpamDefenseCopy.resource("spam_group_$group")?.let { resource ->
                    Text(
                        text = stringResource(resource),
                        style = typography.lg,
                        color = tokens.cardForeground,
                    )
                }

                descriptors.forEach { descriptor ->
                    SpamSettingRow(
                        descriptor = descriptor,
                        settings = draft,
                        enabled = manage is ManageDecision.Allowed,
                        onChange = { updated -> draft = updated },
                    )
                }
            }

        ManageGate(manage) {
            Button(onClick = { onSave(draft) }, enabled = manage is ManageDecision.Allowed) {
                // Reuses the trust editor's save label: the same act, the same word, and one fewer
                // string for a translator to keep in step.
                Text(text = stringResource(Res.string.moderation_trust_save))
            }
        }
    }
}

/**
 * One control, with its explanation and the cost of moving it. Toggles render as a switch; everything
 * else as a bounded number field. The bounds come from the server, so the form cannot start rejecting
 * values the backend accepts.
 */
@Composable
private fun SpamSettingRow(
    descriptor: SpamSettingDescriptor,
    settings: SpamDefenseSettings,
    enabled: Boolean,
    onChange: (SpamDefenseSettings) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label = SpamDefenseCopy.resource(descriptor.labelKey) ?: return
    val explanation = SpamDefenseCopy.resource(descriptor.explanationKey)
    val cost = SpamDefenseCopy.resource(descriptor.costKey)

    Column(
        modifier = Modifier.fillMaxWidth().padding(vertical = spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        if (descriptor.isToggle) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                Switch(
                    checked = SpamDefenseValues.boolean(settings, descriptor.key),
                    onCheckedChange = { value ->
                        onChange(SpamDefenseValues.withBoolean(settings, descriptor.key, value))
                    },
                    enabled = enabled,
                )
                Text(
                    text = stringResource(label),
                    style = typography.base,
                    color = tokens.cardForeground,
                )
            }
            explanation?.let {
                Text(text = stringResource(it), style = typography.sm, color = tokens.mutedForeground)
            }
        } else {
            var text: String by
                remember(settings, descriptor.key) {
                    mutableStateOf(SpamDefenseValues.text(settings, descriptor.key))
                }

            AppTextField(
                value = text,
                onValueChange = { entered ->
                    text = entered
                    SpamDefenseValues.withText(settings, descriptor.key, entered)?.let(onChange)
                },
                label = stringResource(label),
                enabled = enabled,
                supportingText = explanation?.let { stringResource(it) },
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                modifier = Modifier.fillMaxWidth(),
            )
        }

        // The cost of moving it, always, for every control. This is the line that turns a settings page
        // into something an operator can tune honestly rather than guess at.
        cost?.let {
            Text(text = stringResource(it), style = typography.sm, color = tokens.mutedForeground)
        }
    }
}
