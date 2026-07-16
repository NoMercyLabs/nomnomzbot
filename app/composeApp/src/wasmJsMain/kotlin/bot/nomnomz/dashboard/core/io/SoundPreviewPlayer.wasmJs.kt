// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.io

import kotlin.js.ExperimentalWasmJsInterop

// Play the clip through a browser <audio>. `url` is relative, so it resolves against the page origin — the same
// bot that served the dashboard — and the stream endpoint is anonymous + range-enabled, so no token is needed.
// Best-effort: any failure (autoplay policy, missing file, decode error) is swallowed so a Preview click never
// throws back into Compose. `url` is the actual-fun parameter, marshalled into the js() scope by name.
actual fun playSoundPreview(url: String) {
    js("{ try { var a = new Audio(url); a.play(); } catch (e) {} }")
}
