// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

// The web build is single-origin (frontend.md §6): its backend is ALWAYS the origin that served it, so it can
// restore a session straight from the HttpOnly cookie even when no profile was persisted (e.g. localStorage
// was cleared). Native is multi-origin — the remembered backend comes from the saved profile list, not a
// fixed origin — so it returns null and restore relies on the persisted profile.
expect fun servedOriginProfile(): ConnectionProfile?
