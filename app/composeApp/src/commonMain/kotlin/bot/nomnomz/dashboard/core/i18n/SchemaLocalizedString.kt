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

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.network.LocalizedTextDto
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.allStringResources
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// Resolves a backend-authored schema string (widget settings field label/help, pipeline action field
// description) from its translation KEY to the viewer's locale text (S-SCHEMA-I18N-redesign). The backend
// never serves English/Dutch text for these fields — only a dot-separated KEY, e.g.
// `widget.alerts.events.label` — so translators keep editing exactly one place (strings.xml / values-nl/
// strings.xml), the same file every other dashboard string lives in.
//
// Compose Resources string names must be valid identifiers (no dots), so the mapping from backend key to
// Compose resource name replaces `.` with `_`: `widget.alerts.events.label` -> `widget_alerts_events_label`.
// [Res.allStringResources] is the generated runtime lookup Compose Resources itself builds from strings.xml, so
// a key with no matching resource is a real authoring gap (missed by `SchemaLocalizationManifestTests`/
// `SchemaLocalizationManifestTest` some other way) rather than a typo in this file — it falls back to the raw
// key so a broken translation is visibly wrong instead of blank.
@OptIn(ExperimentalResourceApi::class)
@Composable
fun resolveSchemaString(key: String?): String {
    if (key.isNullOrBlank()) return ""
    val resource: StringResource = Res.allStringResources[schemaResourceName(key)] ?: return key
    return stringResource(resource)
}

/** Resolves a [LocalizedTextDto] (nullable) to display text, or "" when absent. */
@Composable
fun resolveSchemaString(text: LocalizedTextDto?): String = resolveSchemaString(text?.key)

/** The Compose Resources string name a backend dot-separated translation key maps to. */
fun schemaResourceName(key: String): String = key.replace('.', '_')
