// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSettingsSchemaDto

/** The server's answer for a widget that declares no settings (no first-party schema, no settings.json). */
internal const val NO_SETTINGS_SCHEMA_CODE: String = "WIDGET_NO_SETTINGS_SCHEMA"

/** What the settings dialog shows for a schema load result. */
internal enum class SettingsLoadView {
    Loading,
    NoSettings,
    Error,
    Form,
}

internal fun settingsLoadView(result: ApiResult<WidgetSettingsSchemaDto>?): SettingsLoadView =
    when (result) {
        null -> SettingsLoadView.Loading
        is ApiResult.Failure ->
            if (result.error.code == NO_SETTINGS_SCHEMA_CODE) SettingsLoadView.NoSettings else SettingsLoadView.Error
        is ApiResult.Ok -> SettingsLoadView.Form
    }
