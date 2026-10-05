// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.songrequests_banger_auto_create
import nomnomzbot.composeapp.generated.resources.songrequests_banger_clear
import nomnomzbot.composeapp.generated.resources.songrequests_banger_hint
import nomnomzbot.composeapp.generated.resources.songrequests_banger_playlist_label
import nomnomzbot.composeapp.generated.resources.songrequests_banger_playlist_none
import nomnomzbot.composeapp.generated.resources.songrequests_banger_provider_label
import nomnomzbot.composeapp.generated.resources.songrequests_banger_provider_none
import nomnomzbot.composeapp.generated.resources.songrequests_banger_title
import org.jetbrains.compose.resources.stringResource

/**
 * Settings for the `!banger` command: the playlist it adds to, the provider that playlist lives on, and
 * whether `!banger` creates one on first use. Every control saves at once (no batch Save), so this card has
 * no primary button; the only button is the ghost "clear" action, shown only while a choice exists.
 */
@Composable
internal fun BangerSection(
    config: MusicConfig,
    playlists: List<MusicPlaylist>,
    configure: ManageDecision,
    onChoose: (playlist: MusicPlaylist) -> Unit,
    onClear: () -> Unit,
    onAutoCreate: (Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var menuOpen: Boolean by remember { mutableStateOf(false) }
    val chosenId: String? = config.bangerPlaylistId?.takeIf { it.isNotBlank() }
    // A saved id that the account list does not contain (list unreadable, playlist deleted) still shows as itself.
    val chosenLabel: String =
        chosenId?.let { id -> playlists.firstOrNull { it.id == id }?.name ?: id }
            ?: stringResource(Res.string.songrequests_banger_playlist_none)
    val provider: String =
        config.bangerPlaylistProvider?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.songrequests_banger_provider_none)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.songrequests_banger_title),
                style = typography.base.copy(fontWeight = FontWeight.SemiBold),
                color = tokens.cardForeground,
            )
            Text(
                text = stringResource(Res.string.songrequests_banger_hint),
                style = typography.sm,
                color = tokens.mutedForeground,
            )

            ManageGate(decision = configure) { enabled ->
                AppSelectField(
                    label = stringResource(Res.string.songrequests_banger_playlist_label),
                    value = chosenLabel,
                    expanded = menuOpen,
                    onExpandedChange = { menuOpen = it },
                    modifier = Modifier.fillMaxWidth(),
                    enabled = enabled && playlists.isNotEmpty(),
                    menu = {
                        playlists.forEach { playlist ->
                            DropdownMenuItem(
                                text = { Text(playlist.name, color = tokens.cardForeground) },
                                onClick = {
                                    menuOpen = false
                                    onChoose(playlist)
                                },
                            )
                        }
                    },
                )
            }

            Text(
                text = stringResource(Res.string.songrequests_banger_provider_label, provider),
                style = typography.sm,
                color = tokens.mutedForeground,
            )

            SrToggleRow(
                label = stringResource(Res.string.songrequests_banger_auto_create),
                checked = config.bangerAutoCreate,
                configure = configure,
                onToggle = onAutoCreate,
            )

            if (chosenId != null) {
                ManageGate(decision = configure) { enabled ->
                    TextButton(onClick = onClear, enabled = enabled) {
                        Text(
                            text = stringResource(Res.string.songrequests_banger_clear),
                            color = tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }
}
