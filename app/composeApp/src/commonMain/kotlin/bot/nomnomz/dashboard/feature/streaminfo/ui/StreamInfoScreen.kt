// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.streaminfo.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.settings.ui.CenteredMessage
import bot.nomnomz.dashboard.feature.settings.ui.ErrorContent
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoController
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.settings_loading
import nomnomzbot.composeapp.generated.resources.settings_status_live
import nomnomzbot.composeapp.generated.resources.settings_status_offline
import nomnomzbot.composeapp.generated.resources.shell_nav_stream_info
import org.jetbrains.compose.resources.stringResource

// The Stream page "Info and presets" tab: the broadcast title, category and tags editor plus the live status. It
// moved here from the Settings page. Editing is a Moderator write (the backend re-checks); below that floor the
// fields go read-only with the reason via the gated save bar. The page header and tab strip come from the shell.
@Composable
fun StreamInfoScreen(controller: StreamInfoController, role: ManagementRole?) {
    val state: StreamInfoState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val manage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(LocalSpacing.current.s6),
        verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s6),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_stream_info))
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) {
            when (val current: StreamInfoState = state) {
                is StreamInfoState.Loading -> CenteredMessage(stringResource(Res.string.settings_loading))
                is StreamInfoState.Error ->
                    ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                is StreamInfoState.Ready ->
                    ReadyContent(
                        state = current,
                        manage = manage,
                        onSearchCategories = controller::searchCategories,
                        onSave = { title, gameName, tags -> scope.launch { controller.save(title, gameName, tags) } },
                    )
            }
        }
    }
}

@Composable
private fun ReadyContent(
    state: StreamInfoState.Ready,
    manage: ManageDecision,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onSave: (title: String, gameName: String, tags: List<String>) -> Unit,
) {
    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s4),
    ) {
        StatusBanner(isLive = state.info.isLive)
        StreamInfoEditor(
            state = state,
            manage = manage,
            onSearchCategories = onSearchCategories,
            onSave = onSave,
        )
    }
}

@Composable
private fun StatusBanner(isLive: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(if (isLive) Res.string.settings_status_live else Res.string.settings_status_offline)

    // One node for screen readers: "Live" rather than a disconnected dot + label.
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clearAndSetSemantics { contentDescription = statusText },
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(if (isLive) tokens.primary else tokens.mutedForeground),
            )
            Text(text = statusText, style = typography.xl, color = tokens.cardForeground)
        }
    }
}
