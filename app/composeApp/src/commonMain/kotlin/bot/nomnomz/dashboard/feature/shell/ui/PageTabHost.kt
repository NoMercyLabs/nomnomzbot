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

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute

// The tab strip of a page that has more than one tab. One segmented control (the shared Tabs primitive, no accent
// of its own): the selected tab is the only emphasised segment. A tab click only asks the shell to open that tab's
// route, so the address and the strip never disagree. A page with one tab shows no strip; its address still names
// the tab.
@Composable
fun PageTabHost(
    selected: ShellRoute,
    label: @Composable (ShellRoute) -> String,
    onSelect: (ShellRoute) -> Unit,
    modifier: Modifier = Modifier,
    isOpenable: (ShellRoute) -> Boolean = { true },
    content: @Composable (Modifier) -> Unit,
) {
    val tabs: List<ShellRoute> = ShellRoute.tabsOf(selected.page).filter(isOpenable)
    if (tabs.size < 2) {
        content(modifier)
        return
    }
    val spacing = LocalSpacing.current
    Column(modifier = modifier) {
        TabsList(modifier = Modifier.padding(horizontal = spacing.s6, vertical = spacing.s3)) {
            for (tab: ShellRoute in tabs) {
                TabsTrigger(selected = tab == selected, onClick = { onSelect(tab) }) {
                    Text(text = label(tab))
                }
            }
        }
        content(Modifier.weight(1f).fillMaxWidth())
    }
}
