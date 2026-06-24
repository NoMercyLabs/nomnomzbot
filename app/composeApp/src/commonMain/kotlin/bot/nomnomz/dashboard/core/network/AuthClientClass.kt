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

// The client class the auth calls advertise so the backend can pick the right refresh-token custody
// (frontend.md §6): on the BROWSER build it is "web", which makes the backend keep the refresh token in an
// HttpOnly cookie (JS can't read it → XSS can't steal it) and strip it from the response body; on NATIVE it
// is null — there is no browser XSS surface, so the token rides the body into the OS file/keychain vault.
expect fun authClientClass(): String?
