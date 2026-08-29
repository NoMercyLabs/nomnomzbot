// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed facade over `GET /api/v1/templates/helpers?context=` (S042/S043) — the machine-readable
// template helper registry (90+ placeholders across 12 namespaces) that TemplateHelperValidator uses at
// save time. The dashboard's shared "All helpers" dialog (TemplateHelpersDialog.kt) is the ONLY consumer:
// every template text field (commands, event responses, timers, pipelines, chat triggers, giveaways,
// Discord, rewards) asks this API for the valid set for its own [TemplateHelperContext] rather than
// hand-listing placeholders — the backend registry is the single source of truth (S043).
//
// Backend route (TemplatesController):
//   GET /api/v1/templates/helpers?context=<TemplateHelperContext>  →  StatusResponseDto<List<TemplateHelperDto>>
interface TemplateHelpersApi {
    /** The full valid helper set for [context] — global namespaces plus whatever that surface seeds. */
    suspend fun helpers(context: TemplateHelperContext): ApiResult<List<TemplateHelperDto>>
}

class RestTemplateHelpersApi(private val client: ApiClient) : TemplateHelpersApi {
    override suspend fun helpers(context: TemplateHelperContext): ApiResult<List<TemplateHelperDto>> =
        client.getEnvelope("api/v1/templates/helpers?context=${context.wireName}")
}

/**
 * Mirrors the backend `TemplateHelperContext` enum (`NomNomzBot.Application.Abstractions.Templating`)
 * one-to-one — a surface a template string can be saved for, which determines the valid helper keys.
 * [wireName] is the exact C# enum member name the backend's `Enum.TryParse` expects on the query string.
 */
enum class TemplateHelperContext(val wireName: String) {
    Command("Command"),
    EventResponse("EventResponse"),
    Timer("Timer"),
    Pipeline("Pipeline"),
    Discord("Discord"),
    Webhook("Webhook"),
}

/**
 * One entry of the helper registry (backend `TemplateHelperDto`). [key] is the placeholder text as
 * written in a template (without the surrounding braces, e.g. `user.name` or `random.number.<n>` for a
 * prefixed family); [descriptionKey] is the i18n key the dashboard resolves via [resolveSchemaString] —
 * the backend never ships resolved English/Dutch text for a user-facing string.
 */
@Serializable
data class TemplateHelperDto(
    val key: String = "",
    val descriptionKey: String = "",
)
