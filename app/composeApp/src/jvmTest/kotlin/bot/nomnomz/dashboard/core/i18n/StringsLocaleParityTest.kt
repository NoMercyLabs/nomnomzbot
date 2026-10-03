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
import kotlin.test.assertTrue
import kotlin.test.fail

// S-EDITOR-I18N: the English and Dutch resource folders must define exactly the same keys. A key present in only
// one language ships either an English-only control on a Dutch dashboard or a Dutch string nothing reads.
class StringsLocaleParityTest {
    private val resourceTags: List<String> = listOf("string", "plurals", "string-array")

    @Test
    fun english_and_dutch_define_the_same_resource_keys() {
        val english: Set<String> = namesInFolder("values")
        val dutch: Set<String> = namesInFolder("values-nl")
        assertTrue(english.isNotEmpty(), "values/ should define resources")

        val missingInDutch: List<String> = (english - dutch).sorted()
        val missingInEnglish: List<String> = (dutch - english).sorted()

        if (missingInDutch.isNotEmpty() || missingInEnglish.isNotEmpty()) {
            fail(
                "English and Dutch resource keys differ:\n" +
                    (if (missingInDutch.isNotEmpty()) "  missing in values-nl: $missingInDutch\n" else "") +
                    (if (missingInEnglish.isNotEmpty()) "  missing in values: $missingInEnglish\n" else ""),
            )
        }
    }

    private fun namesInFolder(variant: String): Set<String> =
        fromRepoRoot("app/composeApp/src/commonMain/composeResources/$variant")
            .listFiles { file -> file.extension == "xml" }
            .orEmpty()
            .flatMap { namesIn(it) }
            .toSet()

    private fun namesIn(file: File): Set<String> {
        val document = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        return buildSet {
            for (tag in resourceTags) {
                val nodes = document.getElementsByTagName(tag)
                for (i in 0 until nodes.length) add((nodes.item(i) as Element).getAttribute("name"))
            }
        }
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
