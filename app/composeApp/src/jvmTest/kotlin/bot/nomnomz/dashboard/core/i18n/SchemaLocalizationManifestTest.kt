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
import java.util.Locale
import javax.xml.parsers.DocumentBuilderFactory
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.allStringResources
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.getString
import org.jetbrains.compose.resources.getSystemResourceEnvironment
import org.w3c.dom.Element
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// S-SCHEMA-I18N-redesign's two-part drift guard, frontend half (backend counterpart:
// SchemaLocalizationManifestTests.cs). The backend authors a translation KEY ONLY for every widget settings
// field label/help and pipeline action field description — never English/Dutch text — so the mechanism this
// guards is: every key the backend can possibly emit (the committed manifest, server/i18n/schema-i18n-
// keys.manifest.json, regenerated from the REAL backend schema) resolves through the REAL Compose Resources
// pipeline to non-blank text in BOTH languages. A key present in the manifest but missing from strings.xml (or
// values-nl/strings.xml) fails here, at build time — never a blank/English-only control shipped to a Dutch
// dashboard.
@OptIn(ExperimentalResourceApi::class)
class SchemaLocalizationManifestTest {

    @Test
    fun every_manifest_key_resolves_to_non_blank_text_in_both_languages() {
        val keys: List<String> = readManifestKeys()
        assertTrue(keys.isNotEmpty(), "the manifest should carry the real backend schema's translation keys")

        val englishNames: Set<String> = stringNamesIn(stringsXmlFile("values"))
        val dutchNames: Set<String> = stringNamesIn(stringsXmlFile("values-nl"))

        val missingEnglish: List<String> = keys.filter { schemaResourceName(it) !in englishNames }
        val missingDutch: List<String> = keys.filter { schemaResourceName(it) !in dutchNames }

        if (missingEnglish.isNotEmpty() || missingDutch.isNotEmpty()) {
            fail(
                "Schema i18n keys missing from the dashboard's translation files:\n" +
                    (if (missingEnglish.isNotEmpty()) "  values/strings.xml missing: $missingEnglish\n" else "") +
                    (if (missingDutch.isNotEmpty()) "  values-nl/strings.xml missing: $missingDutch\n" else "") +
                    "Add the string(s) to both files (English and Dutch — never English-only).",
            )
        }
    }

    // Proves the mechanism end-to-end for a REAL widget field key, through the actual packaged Compose
    // Resources bundle — not a mock — by forcing the JVM default locale to Dutch and reading the resource the
    // exact way `resolveSchemaString` does (`Res.allStringResources[name]` then load), same as `stringResource`
    // resolves at runtime minus the Composable wrapper.
    @Test
    fun a_widget_settings_field_key_renders_dutch_text() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                Res.allStringResources[schemaResourceName("widget.alerts.events.label")]
                    ?: fail("widget_alerts_events_label is not a known Compose string resource")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals("Waarschuwingstypen", resolved)
        } finally {
            Locale.setDefault(original)
        }
    }

    // Proof for a select/multiselect field's OPTION label — the surface S-SCHEMA-I18N-b(a) migrated (options
    // used to serve a hardcoded English display label straight off the wire; now they serve a translation key
    // like every other schema string).
    @Test
    fun a_dropdown_option_key_renders_dutch_text() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                Res.allStringResources[schemaResourceName("widget.chat_box.theme.option.dark")]
                    ?: fail("widget_chat_box_theme_option_dark is not a known Compose string resource")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals("Donker", resolved)
        } finally {
            Locale.setDefault(original)
        }
    }

    // Same proof for a pipeline action field's help key — the OTHER schema surface this slice migrated.
    @Test
    fun a_pipeline_action_field_key_renders_dutch_text() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                Res.allStringResources[schemaResourceName("pipeline.ban.reason.help")]
                    ?: fail("pipeline_ban_reason_help is not a known Compose string resource")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals(
                "Wordt aan de kijker getoond en vastgelegd in de moderatiegeschiedenis.",
                resolved,
            )
        } finally {
            Locale.setDefault(original)
        }
    }

    // Same proof for the pipeline block PALETTE's action-level strings (S-SCHEMA-I18N-d): the category heading
    // shared by every chat action, and one action's own description — both LocalizedText keys sourced from
    // ICommandAction.Category/Description, resolved the same way every other schema string is.
    @Test
    fun a_pipeline_category_heading_renders_dutch_text() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                Res.allStringResources[schemaResourceName("pipeline.category.chat")]
                    ?: fail("pipeline_category_chat is not a known Compose string resource")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals("Chat", resolved)
        } finally {
            Locale.setDefault(original)
        }
    }

    @Test
    fun a_pipeline_action_name_renders_dutch_text() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                Res.allStringResources[schemaResourceName("pipeline.ban.description")]
                    ?: fail("pipeline_ban_description is not a known Compose string resource")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals("Ban een kijker uit de chat", resolved)
        } finally {
            Locale.setDefault(original)
        }
    }

    private fun readManifestKeys(): List<String> {
        val json: String = manifestFile().readText()
        return Json.parseToJsonElement(json).jsonObject["keys"]!!.jsonArray.map { it.jsonPrimitive.content }
    }

    private fun stringNamesIn(file: File): Set<String> {
        val document =
            DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val nodes = document.getElementsByTagName("string")
        return buildSet {
            for (i in 0 until nodes.length) {
                val element = nodes.item(i) as Element
                add(element.getAttribute("name"))
            }
        }
    }

    /** Walk up from the test working directory to the committed manifest, so the test is location-independent. */
    private fun manifestFile(): File = fromRepoRoot("server/i18n/schema-i18n-keys.manifest.json")

    private fun stringsXmlFile(variant: String): File =
        fromRepoRoot("app/composeApp/src/commonMain/composeResources/$variant/strings.xml")

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
