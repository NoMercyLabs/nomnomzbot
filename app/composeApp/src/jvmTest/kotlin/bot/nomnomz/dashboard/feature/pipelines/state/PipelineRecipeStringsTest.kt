// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.state

import java.io.File
import kotlin.test.Test
import kotlin.test.assertTrue

/** Every recipe text key exists in the English AND the Dutch strings file. */
class PipelineRecipeStringsTest {

    private fun stringsFile(folder: String): String =
        File("src/commonMain/composeResources/$folder/strings.xml").readText()

    @Test
    fun every_recipe_key_is_in_en_and_nl() {
        val names: List<String> =
            PipelineRecipes.all.flatMap { listOf(it.title.key, it.description.key) + it.replies.map { r -> r.key } }
        assertTrue(names.size == 14, "expected 4 titles, 4 descriptions and 6 replies, got ${names.size}")
        for (folder in listOf("values", "values-nl")) {
            val xml: String = stringsFile(folder)
            for (name in names) assertTrue(xml.contains("<string name=\"$name\">"), "$folder is missing $name")
        }
    }
}
