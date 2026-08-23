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

import java.util.Properties

/**
 * The desktop app's version, stamped from the Gradle build (`composeApp/build.gradle.kts`'s
 * `version`) rather than hardcoded here (S111c). `generateAppVersionResource` (the Gradle task)
 * writes `app-version.properties` into the jvm resources at build time; this reads it back off
 * the classpath. A resource-less classpath (a test run before the resource-generating task, or a
 * packaging regression) falls back to [FALLBACK] rather than crashing.
 */
object AppVersion {

    const val FALLBACK: String = "dev"

    val current: String by lazy { readFromClasspath() ?: FALLBACK }

    private fun readFromClasspath(): String? =
        runCatching {
            AppVersion::class.java.classLoader
                ?.getResourceAsStream("app-version.properties")
                ?.use { stream ->
                    Properties().apply { load(stream) }.getProperty("version")
                }
                ?.takeIf { it.isNotBlank() }
        }.getOrNull()
}
