// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

// The Plane-B management role the shell gates on (roles-permissions.md / frontend-ia.md §1). The streamer
// and any delegated manager use the SAME shell; nav and actions show/hide/disable by comparing the caller's
// role [level] against a page's read floor and an action's manage floor. The frontend gate is UX only — the
// backend re-checks every write (frontend-ia.md §7).
enum class ManagementRole(val level: Int) {
    Moderator(10),
    SuperMod(20),
    Editor(30),
    Broadcaster(40),
}
