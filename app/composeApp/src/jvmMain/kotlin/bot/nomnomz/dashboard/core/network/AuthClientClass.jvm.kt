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

// Desktop is not a browser — no HttpOnly-cookie custody, no XSS surface. The auth calls advertise no client
// class, so the backend returns the refresh token in the body for the OS file/keychain vault to hold.
actual fun authClientClass(): String? = null
