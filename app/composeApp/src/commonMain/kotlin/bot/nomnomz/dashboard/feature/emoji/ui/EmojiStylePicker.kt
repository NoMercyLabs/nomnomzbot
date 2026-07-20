// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.emoji.ui

import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.feature.emoji.state.EmojiStyle
import bot.nomnomz.dashboard.feature.emoji.state.EmojiStyleController
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.emoji_style_color
import nomnomzbot.composeapp.generated.resources.emoji_style_monochrome
import org.jetbrains.compose.resources.stringResource

// The emoji-style selector — Color · Monochrome, as a shadcn segmented control ([TabsList] + [TabsTrigger],
// same DS component the app uses elsewhere for a one-of-N pick). It mirrors LanguagePicker: read the active
// style from the controller and, on a pick, persist + apply it live (the whole type scale swaps its emoji
// font with no restart). Color is the default; Monochrome is the fallback when a browser/Skia build can't
// render the color (COLR) emoji face and would otherwise show boxes.
@Composable
fun EmojiStylePicker(controller: EmojiStyleController, modifier: Modifier = Modifier) {
    val current: EmojiStyle by controller.current.collectAsStateWithLifecycle()

    TabsList(modifier = modifier) {
        TabsTrigger(
            selected = current == EmojiStyle.Color,
            onClick = { controller.select(EmojiStyle.Color) },
        ) {
            Text(text = stringResource(Res.string.emoji_style_color))
        }
        TabsTrigger(
            selected = current == EmojiStyle.Monochrome,
            onClick = { controller.select(EmojiStyle.Monochrome) },
        ) {
            Text(text = stringResource(Res.string.emoji_style_monochrome))
        }
    }
}
