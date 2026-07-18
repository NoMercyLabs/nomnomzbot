// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

/**
 * Copy [text] to the system clipboard and report whether it actually landed.
 *
 * The web build must work when the self-host dashboard is served over plain `http://<lan-ip>:5080` — a
 * NON-secure browsing context where `navigator.clipboard` is undefined. Compose's own `LocalClipboardManager`
 * routes only through that unavailable API, so a copy there silently no-ops while the UI still flashes
 * "copied". This seam uses the legacy `document.execCommand('copy')` path (a transient off-screen textarea),
 * which works in insecure contexts too and returns a real success signal — so callers can confirm truthfully
 * instead of lying. The values these buttons carry (OAuth redirect URIs, automation token secrets, webhook
 * signing secrets, pairing codes) are shown once and unrecoverable if the copy fails.
 *
 * Returns `true` on a confirmed copy, `false` when the platform could not write the clipboard.
 */
expect fun copyToClipboard(text: String): Boolean
