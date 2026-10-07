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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.state_clear_filter
import nomnomzbot.composeapp.generated.resources.state_no_results
import org.jetbrains.compose.resources.stringResource

/**
 * The shared never-used empty state (psychology spec X3): what this page is for, and exactly one primary action
 * slot. The [action] is the page's one primary action (usually its "New ..." button); pass nothing when the
 * caller may not create. Never use this for a filtered-to-nothing list: that is [NoResultsState].
 */
@Composable
fun EmptyState(
    title: String,
    description: String,
    modifier: Modifier = Modifier,
    action: (@Composable () -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = modifier.fillMaxSize().padding(spacing.s6), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = title,
                style = typography.lg.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(
                text = description,
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            if (action != null) {
                Box(modifier = Modifier.padding(top = spacing.s2)) { action() }
            }
        }
    }
}

/**
 * The shared filtered-to-nothing state: "No results for '<query>'" with one Clear filter action. The list
 * exists; the filter hides it, so the way out is clearing the filter, not creating something.
 */
@Composable
fun NoResultsState(query: String, onClearFilter: () -> Unit, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = modifier.fillMaxSize().padding(spacing.s6), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.state_no_results, query),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            Button(onClick = onClearFilter, variant = ButtonVariant.Outline) {
                Text(text = stringResource(Res.string.state_clear_filter))
            }
        }
    }
}
