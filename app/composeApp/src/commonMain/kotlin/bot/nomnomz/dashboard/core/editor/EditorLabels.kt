// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.allStringResources
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.getString

// The words of the served editor page, in the dashboard's current language. The page (editor.js) names every
// text it shows by a camelCase label id; the string resources carry them as `editor_<snake_case>` keys, so the
// two sides map by name alone and a new label needs no Kotlin change. Sent in the open payload's `labels`.
@OptIn(ExperimentalResourceApi::class)
object EditorLabels {
    const val KEY_PREFIX: String = "editor_"

    /** The string resource name for a label id: `themeGitHubLight` becomes `editor_theme_git_hub_light`. */
    fun resourceName(id: String): String =
        KEY_PREFIX +
            buildString {
                for (character: Char in id) {
                    if (character.isUpperCase()) append('_')
                    append(character.lowercaseChar())
                }
            }

    /** The label id for a string resource name, or null when the name is not an editor label. */
    fun idOf(resourceName: String): String? {
        if (!resourceName.startsWith(KEY_PREFIX)) return null
        val parts: List<String> = resourceName.removePrefix(KEY_PREFIX).split('_').filter { it.isNotEmpty() }
        if (parts.isEmpty()) return null
        return parts.first() + parts.drop(1).joinToString("") { part -> part.replaceFirstChar { it.uppercaseChar() } }
    }

    /** Every editor label in the current language, keyed by label id. */
    suspend fun resolve(): Map<String, String> {
        val labels: MutableMap<String, String> = mutableMapOf()
        for ((name: String, resource) in Res.allStringResources) {
            val id: String = idOf(name) ?: continue
            labels[id] = getString(resource)
        }
        return labels
    }
}
