// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import bot.nomnomz.dashboard.core.designsystem.component.RadioButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineRecipe
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineRecipes
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_empty_desc
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_empty_title
import nomnomzbot.composeapp.generated.resources.pipelines_recipe_picker_label
import org.jetbrains.compose.resources.stringResource

/**
 * The "how should this pipeline start" choice in the create dialog: "Start empty" (a null [selected]) or one of
 * the [PipelineRecipes]. A radio list, because exactly one option applies. The chosen row gets the neutral
 * `muted` fill; the dialog's Create button stays the only primary action.
 */
@Composable
internal fun PipelineRecipePicker(
    selected: PipelineRecipe?,
    onSelect: (PipelineRecipe?) -> Unit,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(
            text = stringResource(Res.string.pipelines_recipe_picker_label),
            style = typography.sm,
            color = tokens.mutedForeground,
            // Lines up with the field labels above it, which sit inside their fields.
            modifier = Modifier.padding(horizontal = spacing.s4),
        )
        Column(modifier = Modifier.selectableGroup(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            RecipeOption(
                selected = selected == null,
                title = stringResource(Res.string.pipelines_recipe_empty_title),
                description = stringResource(Res.string.pipelines_recipe_empty_desc),
                onClick = { onSelect(null) },
            )
            for (recipe: PipelineRecipe in PipelineRecipes.all) {
                RecipeOption(
                    selected = selected?.id == recipe.id,
                    title = stringResource(recipe.title),
                    description = stringResource(recipe.description),
                    onClick = { onSelect(recipe) },
                )
            }
        }
    }
}

@Composable
private fun RecipeOption(selected: Boolean, title: String, description: String, onClick: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val shape = RoundedCornerShape(tokens.radius.md)

    Row(
        modifier =
            Modifier.fillMaxWidth()
                .clip(shape)
                .background(if (selected) tokens.muted else Color.Transparent)
                .selectable(selected = selected, role = Role.RadioButton, onClick = onClick)
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        RadioButton(selected = selected, onClick = null)
        Column {
            Text(text = title, style = typography.sm, color = tokens.foreground)
            Text(text = description, style = typography.sm, color = tokens.mutedForeground)
        }
    }
}
