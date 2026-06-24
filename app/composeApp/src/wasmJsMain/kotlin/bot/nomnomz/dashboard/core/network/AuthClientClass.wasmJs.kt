// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

// The browser build advertises "web" so the backend custodies the refresh token in an HttpOnly + Secure
// cookie (unreadable by JS) and never returns it in the response body — the browser attaches it automatically
// on the same-origin refresh call.
actual fun authClientClass(): String? = "web"
