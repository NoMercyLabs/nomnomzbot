// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.app_name
import nomnomzbot.composeapp.generated.resources.shell_action_disconnect
import nomnomzbot.composeapp.generated.resources.shell_content_empty
import nomnomzbot.composeapp.generated.resources.shell_nav_commands
import nomnomzbot.composeapp.generated.resources.shell_nav_community
import nomnomzbot.composeapp.generated.resources.shell_nav_dashboard
import nomnomzbot.composeapp.generated.resources.shell_nav_settings
import nomnomzbot.composeapp.generated.resources.shell_topbar_title

// The authenticated Main shell skeleton (frontend.md §5): persistent left sidebar +
// topbar + empty content area. FOUNDATION slice — the content NavHost, real nav graph,
// and dashboard widgets land in later slices; the chrome is real and on-token here.
@Composable
fun ShellScreen(onDisconnect: () -> Unit) {
    val tokens = LocalTokens.current

    Row(modifier = Modifier.fillMaxSize().background(tokens.background)) {
        Sidebar()
        Column(modifier = Modifier.fillMaxSize()) {
            TopBar(onDisconnect = onDisconnect)
            EmptyContent()
        }
    }
}

@Composable
private fun Sidebar() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxHeight()
            .width(spacing.s24 * 2.5f)
            .background(tokens.sidebar)
            .padding(spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(
            text = stringResource(Res.string.app_name),
            style = typography.lg,
            color = tokens.sidebarForeground,
            modifier = Modifier.padding(
                start = spacing.s2,
                top = spacing.s2,
                bottom = spacing.s3,
            ),
        )
        NavItem(Res.string.shell_nav_dashboard, selected = true)
        NavItem(Res.string.shell_nav_commands, selected = false)
        NavItem(Res.string.shell_nav_community, selected = false)
        NavItem(Res.string.shell_nav_settings, selected = false)
    }
}

@Composable
private fun NavItem(label: StringResource, selected: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val container: Color = if (selected) tokens.sidebarAccent else Color.Transparent
    val content: Color = if (selected) tokens.sidebarAccentForeground else tokens.sidebarForeground

    Box(
        modifier = Modifier
            .fillMaxWidth()
            .background(container, RoundedCornerShape(tokens.radius.md))
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    ) {
        Text(text = stringResource(label), style = typography.sm, color = content)
    }
}

@Composable
private fun TopBar(onDisconnect: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .height(spacing.s16)
                .background(tokens.background)
                .padding(horizontal = spacing.s6),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(Res.string.shell_topbar_title),
                style = typography.xl,
                color = tokens.foreground,
            )
            TextButton(onClick = onDisconnect) {
                Text(text = stringResource(Res.string.shell_action_disconnect))
            }
        }
        HorizontalDivider(color = tokens.border)
    }
}

@Composable
private fun EmptyContent() {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = stringResource(Res.string.shell_content_empty),
            style = typography.base,
            color = tokens.mutedForeground,
            textAlign = TextAlign.Center,
        )
    }
}
