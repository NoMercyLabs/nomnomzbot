// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.platform

import java.io.File
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/** Persisted window geometry — position/size are logical (dp) pixels, matching Compose's DpSize/DpOffset. */
@Serializable
data class WindowGeometry(
    val x: Float,
    val y: Float,
    val width: Float,
    val height: Float,
    val maximized: Boolean,
)

/** One on-screen rectangle, used to sanity-check a restored geometry against the monitors actually present. */
data class ScreenBounds(val x: Float, val y: Float, val width: Float, val height: Float) {
    fun intersects(geometry: WindowGeometry): Boolean {
        val geometryRight: Float = geometry.x + geometry.width
        val geometryBottom: Float = geometry.y + geometry.height
        val boundsRight: Float = x + width
        val boundsBottom: Float = y + height
        return geometry.x < boundsRight && geometryRight > x && geometry.y < boundsBottom && geometryBottom > y
    }
}

/**
 * Desktop window-state persistence (S111c) — remembers size, position, and maximized-ness across
 * restarts, stored beside the other desktop state files (same [DesktopDataDir] base as the token
 * vault, saved connections, etc). A missing or corrupt file yields [defaultGeometry] rather than
 * crashing the app; a saved position that no longer intersects any currently-connected monitor
 * (external monitor unplugged, resolution changed) is discarded the same way, since restoring a
 * window fully off-screen would leave the operator unable to reach it.
 */
class WindowStateStore internal constructor(private val file: File, private val defaultGeometry: WindowGeometry) {

    constructor(defaultGeometry: WindowGeometry) : this(File(DesktopDataDir.resolve(), "window-state.json"), defaultGeometry)

    private val json: Json = Json { ignoreUnknownKeys = true }

    fun save(geometry: WindowGeometry) {
        runCatching {
            file.parentFile?.mkdirs()
            file.writeText(json.encodeToString(WindowGeometry.serializer(), geometry))
        }
    }

    /** Reads the persisted geometry, falling back to [defaultGeometry] on a missing/corrupt file. */
    fun load(): WindowGeometry =
        runCatching {
            if (!file.exists()) return@runCatching defaultGeometry
            json.decodeFromString(WindowGeometry.serializer(), file.readText())
        }.getOrDefault(defaultGeometry)

    /**
     * Reads the persisted geometry and validates it against the monitors actually present. A
     * maximized window is always honoured (its position doesn't matter once maximized); a
     * non-maximized window whose rectangle intersects none of [availableScreens] falls back to
     * [defaultGeometry] rather than opening somewhere the operator can't see or reach it.
     */
    fun loadSanitized(availableScreens: List<ScreenBounds>): WindowGeometry {
        val loaded: WindowGeometry = load()
        if (loaded.maximized) return loaded
        if (availableScreens.isEmpty()) return loaded
        val onScreen: Boolean = availableScreens.any { it.intersects(loaded) }
        return if (onScreen) loaded else defaultGeometry
    }
}
