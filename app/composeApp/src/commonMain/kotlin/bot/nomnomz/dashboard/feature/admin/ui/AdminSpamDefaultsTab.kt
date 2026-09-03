// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import bot.nomnomz.dashboard.feature.moderation.ui.SpamDefenseSection
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.spam_defaults_explain
import org.jetbrains.compose.resources.stringResource

/**
 * Platform-wide spam-defence defaults — the sixth and last spam surface.
 *
 * It renders the **same** editor component as the channel page. That is the point of §6's "two
 * surfaces, identical editor": learning one teaches the other, and a knob cannot exist on the channel
 * page while quietly missing from the admin one. Building a second form here is exactly how the two
 * would drift into configuring different machines.
 *
 * The lead paragraph states the consequence, because this page is the one place where changing a single
 * number moves every channel that has never touched it.
 */
@Composable
internal fun SpamDefaultsTab(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier.fillMaxWidth().padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.spam_defaults_explain),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        // Null while the lazy load is in flight, or if the read failed — the banner above the tabs
        // already carries the error, so this simply renders nothing rather than an empty form the
        // operator could type into and lose.
        val policy = state.spamDefaults ?: return@Column

        Card(modifier = Modifier.fillMaxWidth()) {
            SpamDefenseSection(
                policy = policy,
                // Reaching this tab already required the platform permission the endpoint enforces, so
                // the control-level gate is Allowed; the server still validates every value.
                manage = ManageDecision.Allowed,
                onSave = { settings -> scope.launch { controller.saveSpamDefaults(settings) } },
            )
        }
    }
}
