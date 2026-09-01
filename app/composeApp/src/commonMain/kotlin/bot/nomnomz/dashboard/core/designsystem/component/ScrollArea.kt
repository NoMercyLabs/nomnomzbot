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

import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxScope
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens

/** shadcn ScrollArea orientation (frontend-design-system.md §4, catalogue row). */
enum class ScrollAreaOrientation {
    Vertical,
    Horizontal,
    Both,
}

/**
 * shadcn/ui ScrollArea ported to Compose (frontend-design-system.md §4, catalogue row).
 *
 * Foundation-based — a clipped, scrollable [Box] over Compose's own [verticalScroll] /
 * [horizontalScroll] modifiers (no custom scrollbar widget exists in Compose Multiplatform
 * common code, so the "styled scrollbar" contract is satisfied by the platform's native
 * over-scroll/scrollbar rendering rather than a bespoke thumb). [orientation] selects which
 * axis (or both) the content scrolls on; content taller/wider than the viewport becomes
 * reachable via scroll/drag/wheel exactly as shadcn's ScrollArea makes overflow reachable.
 */
@Composable
fun ScrollArea(
    modifier: Modifier = Modifier,
    orientation: ScrollAreaOrientation = ScrollAreaOrientation.Vertical,
    content: @Composable BoxScope.() -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val shape: RoundedCornerShape = RoundedCornerShape(tokens.radius.md)

    val verticalState = rememberScrollState()
    val horizontalState = rememberScrollState()

    val scrollModifier: Modifier =
        when (orientation) {
            ScrollAreaOrientation.Vertical -> Modifier.verticalScroll(verticalState)
            ScrollAreaOrientation.Horizontal -> Modifier.horizontalScroll(horizontalState)
            ScrollAreaOrientation.Both ->
                Modifier.verticalScroll(verticalState).horizontalScroll(horizontalState)
        }

    Box(
        modifier =
            modifier
                .clip(shape)
                .background(tokens.background)
                .then(scrollModifier),
        content = content,
    )
}
