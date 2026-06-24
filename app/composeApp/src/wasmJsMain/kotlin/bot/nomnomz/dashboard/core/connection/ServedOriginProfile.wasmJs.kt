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

import kotlinx.browser.window

// The browser build's backend is the origin that served it. Synthesizing the profile from the live origin
// lets restore recover the session from the HttpOnly cookie even with empty localStorage — the cookie, not a
// stored profile, is what proves the session.
actual fun servedOriginProfile(): ConnectionProfile? {
    val origin: String = window.location.origin
    return ConnectionProfile(
        id = "served-origin",
        displayName = origin,
        baseUrl = origin,
        source = ProfileSource.ServedOrigin,
    )
}
