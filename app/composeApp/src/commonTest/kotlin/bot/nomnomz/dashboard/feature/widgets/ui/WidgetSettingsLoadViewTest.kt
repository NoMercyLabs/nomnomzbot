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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSettingsSchemaDto
import kotlin.test.Test
import kotlin.test.assertEquals

class WidgetSettingsLoadViewTest {
    private fun failure(code: String?): ApiResult<WidgetSettingsSchemaDto> =
        ApiResult.Failure(ApiError(404, code, "msg"))

    @Test
    fun noSettingsSchemaCodeShowsTheEmptyState() {
        assertEquals(SettingsLoadView.NoSettings, settingsLoadView(failure("WIDGET_NO_SETTINGS_SCHEMA")))
    }

    @Test
    fun anyOtherFailureKeepsTheErrorState() {
        assertEquals(SettingsLoadView.Error, settingsLoadView(failure("FORBIDDEN")))
        assertEquals(SettingsLoadView.Error, settingsLoadView(failure(null)))
    }

    @Test
    fun pendingAndLoadedMapToLoadingAndForm() {
        assertEquals(SettingsLoadView.Loading, settingsLoadView(null))
        assertEquals(SettingsLoadView.Form, settingsLoadView(ApiResult.Ok(WidgetSettingsSchemaDto())))
    }
}
