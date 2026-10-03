// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

import java.io.File
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.fail

// Scripts in this product are JavaScript. A dashboard string that names Lua tells the streamer the wrong
// language, so no string value in either language file may mention it.
class NoLuaInStringsTest {
    @Test
    fun noStringValueNamesLua() {
        val offenders: List<String> =
            listOf("values", "values-nl").flatMap { variant ->
                val folder: File = fromRepoRoot("app/composeApp/src/commonMain/composeResources/$variant")
                folder.listFiles { f -> f.extension == "xml" }.orEmpty().flatMap { file ->
                    val nodes = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
                        .getElementsByTagName("string")
                    (0 until nodes.length)
                        .map { nodes.item(it) as Element }
                        .filter { it.textContent.contains("Lua", ignoreCase = true) }
                        .map { "$variant/${it.getAttribute("name")}" }
                }
            }
        assertEquals(emptyList(), offenders, "strings that name Lua")
    }

    private fun fromRepoRoot(relative: String): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            val candidate = File(dir, relative)
            if (candidate.exists()) return candidate
            dir = dir.parentFile
        }
        fail("Could not locate $relative from ${System.getProperty("user.dir")}")
    }
}
