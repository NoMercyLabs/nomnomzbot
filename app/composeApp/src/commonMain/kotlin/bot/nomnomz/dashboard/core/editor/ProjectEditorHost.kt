// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

// Where the editor page is served from. The web build needs nothing here — it is always served by its bot, so
// `/editor/index.html` is same-origin. The desktop build loads that same page into a native web view and must
// know which bot it is connected to; the composition root points [botOrigin] at the active server connection.
// A provider rather than a value, so switching servers in the profile menu is picked up on the next open.
object ProjectEditorHost {
    var botOrigin: () -> String? = { null }
}
