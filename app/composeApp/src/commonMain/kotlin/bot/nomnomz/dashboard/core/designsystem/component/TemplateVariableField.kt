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

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.i18n.resolveSchemaString
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.feature.pipelines.state.DeclaredVariable
import bot.nomnomz.dashboard.feature.pipelines.state.VariableOption
import bot.nomnomz.dashboard.feature.pipelines.state.braceQuery
import bot.nomnomz.dashboard.feature.pipelines.state.filterVariableOptions
import bot.nomnomz.dashboard.feature.pipelines.state.insertAtCursor
import bot.nomnomz.dashboard.feature.pipelines.state.variableOptions
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.template_variable_declared_by_earlier_step
import nomnomzbot.composeapp.generated.resources.template_variable_list_label
import nomnomzbot.composeapp.generated.resources.template_variable_no_match
import org.jetbrains.compose.resources.stringResource

private val VariableListBorderWidth: Dp = 1.dp

// A template text field that opens an inline variable list when the cursor sits inside a `{fragment`.
// The list sits under the field (not a popup), so it never covers the text being typed. Up/Down move the
// highlight, Enter or a click picks, Esc closes. The caller loads [helpers] (the registry list) and passes the
// variables the earlier steps [declared]; this composable only reads them. Placement: the design-system
// component folder, beside [AppTextField] and [TemplateHelpersLink], which it complements.
@Composable
fun TemplateVariableField(
    value: TextFieldValue,
    onValueChange: (TextFieldValue) -> Unit,
    label: String,
    declared: List<DeclaredVariable>,
    helpers: List<TemplateHelperDto>,
    modifier: Modifier = Modifier,
    supportingText: String? = null,
) {
    val spacing = LocalSpacing.current
    val query: String? = braceQuery(value)
    var dismissed: Boolean by remember { mutableStateOf(false) }
    var highlighted: Int by remember(query) { mutableIntStateOf(0) }

    val options: List<VariableOption> =
        filterVariableOptions(variableOptions(declared, helpers), query.orEmpty())
    val listOpen: Boolean = query != null && !dismissed
    val current: Int = highlighted.coerceIn(0, (options.size - 1).coerceAtLeast(0))

    fun pick(option: VariableOption) {
        dismissed = false
        onValueChange(insertAtCursor(value, "{${option.key}}"))
    }

    Column(
        modifier =
            modifier.onPreviewKeyEvent { event ->
                if (!listOpen || event.type != KeyEventType.KeyDown) return@onPreviewKeyEvent false
                when (event.key) {
                    Key.DirectionDown if options.isNotEmpty() -> {
                        highlighted = (current + 1).coerceAtMost(options.size - 1)
                        true
                    }
                    Key.DirectionUp if options.isNotEmpty() -> {
                        highlighted = (current - 1).coerceAtLeast(0)
                        true
                    }
                    Key.Enter if options.isNotEmpty() -> {
                        pick(options[current])
                        true
                    }
                    Key.Escape -> {
                        dismissed = true
                        true
                    }
                    else -> false
                }
            },
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        AppTextField(
            value = value,
            onValueChange = { next ->
                // Any edit or cursor move re-opens the list: Esc only closes it for the position it was pressed at.
                if (next.text != value.text || next.selection != value.selection) dismissed = false
                onValueChange(next)
            },
            label = label,
            supportingText = supportingText,
            modifier = Modifier.fillMaxWidth(),
        )
        if (listOpen) {
            VariableList(options = options, highlighted = current, onPick = ::pick)
        }
    }
}

@Composable
private fun VariableList(options: List<VariableOption>, highlighted: Int, onPick: (VariableOption) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val containerRadius: Dp = tokens.radius.lg
    // Concentric: the rows sit spacing.s1 inside the container, so their radius is the container's minus that.
    val rowRadius: Dp = containerRadius - spacing.s1
    val listLabel: String = stringResource(Res.string.template_variable_list_label)

    Column(
        modifier =
            Modifier.fillMaxWidth()
                .semantics { contentDescription = listLabel }
                .clip(RoundedCornerShape(containerRadius))
                .background(tokens.popover)
                .border(VariableListBorderWidth,tokens.border, RoundedCornerShape(containerRadius))
                .padding(spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
    ) {
        if (options.isEmpty()) {
            Text(
                text = stringResource(Res.string.template_variable_no_match),
                style = typography.sm,
                color = tokens.mutedForeground,
                modifier = Modifier.padding(spacing.s3),
            )
        }
        options.forEachIndexed { index: Int, option: VariableOption ->
            VariableRow(option = option, selected = index == highlighted, radius = rowRadius, onPick = onPick)
        }
    }
}

@Composable
private fun VariableRow(option: VariableOption, selected: Boolean, radius: Dp, onPick: (VariableOption) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val description: String =
        option.descriptionKey?.let { resolveSchemaString(it) }
            ?: stringResource(Res.string.template_variable_declared_by_earlier_step)

    Column(
        modifier =
            Modifier.fillMaxWidth()
                .clip(RoundedCornerShape(radius))
                .background(if (selected) tokens.muted else tokens.popover)
                .clickable { onPick(option) }
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
    ) {
        Text(text = "{${option.key}}", style = typography.sm, color = tokens.foreground)
        Text(text = description, style = typography.xs, color = tokens.mutedForeground)
        if (option.sample.isNotBlank()) {
            Text(text = option.sample, style = typography.xs, color = tokens.mutedForeground)
        }
    }
}
