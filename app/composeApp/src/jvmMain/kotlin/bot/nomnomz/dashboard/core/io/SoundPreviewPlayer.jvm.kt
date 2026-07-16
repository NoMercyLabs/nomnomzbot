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

// Desktop preview is a follow-on: the JVM has no built-in MP3 decoder and `url` is relative (the desktop client
// would first have to resolve it against the active server connection). No-op for now so the shared code compiles
// and the web build — the primary preview surface — works; wire a desktop audio path when that stack lands.
actual fun playSoundPreview(url: String) {
    // no-op (web-first)
}
