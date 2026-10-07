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
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.state_load_failed_title
import nomnomzbot.composeapp.generated.resources.state_retry
import org.jetbrains.compose.resources.stringResource

/** Test tag on the [LoadFailedState] container. */
const val LoadFailedStateTag: String = "load-failed-state"

/** Test tag on the [LoadFailedState] Retry button. */
const val LoadFailedRetryTag: String = "load-failed-retry"

/**
 * The shared failed-load state: a human [message] saying what could not load, and Retry as the one primary action.
 * A failed load must never look like an empty list, so screens render this instead of their empty state.
 */
@Composable
fun LoadFailedState(message: String, onRetry: () -> Unit, modifier: Modifier = Modifier) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = modifier.fillMaxSize().padding(spacing.s6).testTag(LoadFailedStateTag),
        contentAlignment = Alignment.Center,
    ) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.state_load_failed_title),
                style = typography.lg.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.foreground,
                textAlign = TextAlign.Center,
            )
            Text(text = message, style = typography.sm, color = tokens.destructive, textAlign = TextAlign.Center)
            Box(modifier = Modifier.padding(top = spacing.s2).testTag(LoadFailedRetryTag)) {
                Button(onClick = onRetry) { Text(text = stringResource(Res.string.state_retry)) }
            }
        }
    }
}
