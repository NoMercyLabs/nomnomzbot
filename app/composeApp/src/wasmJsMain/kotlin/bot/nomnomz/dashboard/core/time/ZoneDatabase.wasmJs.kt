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

package bot.nomnomz.dashboard.core.time

import kotlin.js.ExperimentalWasmJsInterop

// kotlinx-datetime on wasmJs is built on js-joda, which ships no IANA zone data. Importing
// @js-joda/timezone registers it, so TimeZone.availableZoneIds and TimeZone.of(id) see every zone
// (the documented wasmJs setup in the kotlinx-datetime README).
@JsModule("@js-joda/timezone")
external object JsJodaTimeZoneModule

/** Registers the IANA zone database; call once before the first zone lookup. */
fun loadZoneDatabase() {
    JsJodaTimeZoneModule
}
