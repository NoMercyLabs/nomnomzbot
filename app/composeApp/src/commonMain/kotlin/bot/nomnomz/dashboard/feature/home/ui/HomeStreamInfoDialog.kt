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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoController
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoState
import bot.nomnomz.dashboard.feature.streaminfo.ui.StreamInfoEditor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.home_stream_close
import nomnomzbot.composeapp.generated.resources.home_stream_section
import nomnomzbot.composeapp.generated.resources.settings_error
import nomnomzbot.composeapp.generated.resources.settings_loading
import org.jetbrains.compose.resources.stringResource

// The Home "Update stream" dialog. It hosts the SAME editor the Stream page tab renders ([StreamInfoEditor]) over
// the SAME [StreamInfoController], so a title or category changed here goes through the same channel stream write
// as one changed on the Stream page. The Home button that opens it is already gated by the title-write action key,
// so the editor is allowed here. [onSaved] fires once per successful save so Home can refresh its stream banner.
// Save is the only filled button; Close is ghost.
@Composable
fun HomeStreamInfoDialog(controller: StreamInfoController, onSaved: () -> Unit, onDismiss: () -> Unit) {
    val state: StreamInfoState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    LaunchedEffect(Unit) { controller.load() }
    LaunchedEffect((state as? StreamInfoState.Ready)?.justSaved) {
        if ((state as? StreamInfoState.Ready)?.justSaved == true) onSaved()
    }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.home_stream_section))
        Column(
            modifier = Modifier.fillMaxWidth().weight(1f, fill = false).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            when (val current: StreamInfoState = state) {
                is StreamInfoState.Loading ->
                    Text(
                        text = stringResource(Res.string.settings_loading),
                        style = LocalTypography.current.base,
                        color = tokens.mutedForeground,
                    )
                is StreamInfoState.Error ->
                    Text(
                        text = stringResource(Res.string.settings_error, current.detail),
                        style = LocalTypography.current.base,
                        color = tokens.destructive,
                    )
                is StreamInfoState.Ready ->
                    StreamInfoEditor(
                        state = current,
                        manage = ManageDecision.Allowed,
                        onSearchCategories = controller::searchCategories,
                        onSave = { title, gameName, tags -> scope.launch { controller.save(title, gameName, tags) } },
                        framed = false,
                    )
            }
        }
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.home_stream_close), maxLines = 1) }
        }
    }
}
