// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.icon

import androidx.compose.runtime.Composable
import org.jetbrains.compose.resources.painterResource

/**
 * Warms the async SVG resource cache for the whole icon pack, once, at app start.
 *
 * `painterResource` on an `.svg` decodes asynchronously (Compose Multiplatform 1.9): the first
 * composition returns an empty placeholder and the real painter arrives a frame later. In a burst of
 * many rows composed at once — the chat scrollback is the worst case — the late update does not repaint
 * the already-laid rows, so they stay blank until re-composed (scroll/hover). Rows that compose against
 * an already-warm cache render correctly on the first frame, which is exactly why live messages were fine.
 *
 * Mounting this at the root touches every pack icon once, so the decode runs and populates the global
 * resource cache before any screen's list renders. The placeholder painters here are never drawn — only
 * the cache-warming side effect of the `painterResource` calls matters. Cheap: the SVGs are tiny and the
 * decoded result is cached for the session.
 */
@Composable
fun IconPreload() {
    AppIcons.all.forEach { icon -> painterResource(icon) }
}
