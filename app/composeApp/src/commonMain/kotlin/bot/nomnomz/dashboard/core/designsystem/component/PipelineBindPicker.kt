// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.PipelineSummary
import kotlinx.coroutines.launch

/**
 * The pipeline-binding picker used everywhere a form binds one pipeline to a trigger — commands, event
 * responses, timers, rewards. Wraps the shared [EntityPickerField] search dropdown over the channel's existing
 * pipelines, PLUS a self-contained "Create a new pipeline" mode: typing a name and confirming calls [onCreate]
 * (the caller's `PipelinesApi.createReturning` write), and on success immediately reports the new pipeline's id
 * through [onSelect] — so binding a pipeline never requires leaving the current dialog to make one first on the
 * Pipelines page (create-and-bind, S046).
 *
 * [selectedId] is the bound pipeline's id, or null when nothing is bound yet. The create-name field and its
 * pending state live entirely inside this component — the caller only supplies the existing [pipelines], the
 * current [selectedId], [onSelect], and [onCreate].
 */
@Composable
fun PipelineBindPicker(
    pipelines: List<PipelineSummary>,
    selectedId: String?,
    onSelect: (String?) -> Unit,
    onCreate: suspend (name: String) -> PipelineSummary?,
    pickLabel: String,
    choosePlaceholder: String,
    createNewLabel: String,
    newNameLabel: String,
    createLabel: String,
    cancelLabel: String,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var creatingNew: Boolean by remember { mutableStateOf(false) }
    var newName: String by remember { mutableStateOf("") }
    var isSubmitting: Boolean by remember { mutableStateOf(false) }

    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Text(text = pickLabel, style = typography.sm, color = tokens.foreground)
        if (creatingNew) {
            // Create-new mode: type a name, then confirm — [onCreate] runs the real write and, on success, binds
            // the new pipeline's server-assigned id immediately. A failure leaves this mode open so the operator
            // can retry (the caller's write already surfaced the error on the frame).
            AppTextField(
                value = newName,
                onValueChange = { newName = it },
                label = newNameLabel,
                modifier = Modifier.fillMaxWidth(),
                enabled = !isSubmitting,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(
                    onClick = {
                        creatingNew = false
                        newName = ""
                    },
                    enabled = !isSubmitting,
                ) {
                    Text(text = cancelLabel, color = tokens.mutedForeground)
                }
                TextButton(
                    onClick = {
                        val name: String = newName.trim()
                        scope.launch {
                            isSubmitting = true
                            val created: PipelineSummary? = onCreate(name)
                            isSubmitting = false
                            if (created != null) {
                                onSelect(created.id)
                                creatingNew = false
                                newName = ""
                            }
                        }
                    },
                    enabled = !isSubmitting && newName.isNotBlank(),
                ) {
                    Text(text = createLabel, color = tokens.primary)
                }
            }
        } else {
            // Pick an EXISTING pipeline through the shared search dropdown (a reference to another table)…
            EntityPickerField(
                items = pipelines,
                selectedId = selectedId,
                onSelect = onSelect,
                idOf = { it.id },
                labelOf = { it.name },
                placeholder = choosePlaceholder,
            )
            // …or switch to authoring a brand-new pipeline right here.
            TextButton(onClick = { creatingNew = true }) {
                AppIcon(AddGlyph, contentDescription = null, tint = tokens.primary, size = spacing.s4)
                Text(text = createNewLabel, color = tokens.primary)
            }
        }
    }
}
