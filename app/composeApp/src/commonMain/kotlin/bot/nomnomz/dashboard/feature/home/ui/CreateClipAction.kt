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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcons
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.LiveOpsClipStub
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.home_live_ops_create_clip
import nomnomzbot.composeapp.generated.resources.home_live_ops_creating_clip
import org.jetbrains.compose.resources.stringResource

// The Home "Create clip" quick action: shows progress while Twitch works, ignores a second tap
// until the first call ends, and shows a refusal inline (no toast).
@Composable
internal fun CreateClipAction(
    createClip: suspend () -> ApiResult<LiveOpsClipStub>,
    onCreated: (LiveOpsClipStub) -> Unit,
    enabled: Boolean,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var pending: Boolean by remember { mutableStateOf(false) }
    var error: String? by remember { mutableStateOf(null) }
    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        QuickActionButton(
            icon = AppIcons.Clips,
            label =
                stringResource(
                    if (pending) Res.string.home_live_ops_creating_clip else Res.string.home_live_ops_create_clip,
                ),
            onClick = {
                if (!pending) {
                    pending = true
                    error = null
                    scope.launch {
                        when (val result: ApiResult<LiveOpsClipStub> = createClip()) {
                            is ApiResult.Ok -> onCreated(result.value)
                            is ApiResult.Failure -> error = result.error.message
                        }
                        pending = false
                    }
                }
            },
            enabled = enabled && !pending,
            loading = pending,
            modifier = Modifier.fillMaxWidth(),
        )
        error?.let { message: String ->
            Text(text = message, style = typography.xs, color = tokens.destructive)
        }
    }
}
