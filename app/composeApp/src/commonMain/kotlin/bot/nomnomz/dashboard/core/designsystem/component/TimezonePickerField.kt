// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.time.ScheduleTimes
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.timezone_picker_empty
import nomnomzbot.composeapp.generated.resources.timezone_picker_label
import nomnomzbot.composeapp.generated.resources.timezone_picker_placeholder
import org.jetbrains.compose.resources.stringResource

/**
 * The shared IANA timezone picker: a searchable list of every zone the platform knows, so a typo can never be
 * typed in. [zone] is the committed zone (null while the user is choosing); the caller seeds it with
 * `ScheduleTimes.defaultZone(saved)` so it starts on the saved zone, or the device zone when none is saved. The
 * Schedule dialogs use it now; the Settings basics card reuses it (S107).
 */
@Composable
fun TimezonePickerField(
    zone: String?,
    onZoneChange: (String?) -> Unit,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
) {
    SearchPickerField(
        search = { query -> ScheduleTimes.matchingZones(query).map { PickerOption(id = it, label = it) } },
        selected = zone?.let { PickerRef(it, it) },
        onSelect = { onZoneChange(it.id) },
        onClear = { onZoneChange(null) },
        modifier = modifier,
        label = stringResource(Res.string.timezone_picker_label),
        placeholder = stringResource(Res.string.timezone_picker_placeholder),
        emptyText = stringResource(Res.string.timezone_picker_empty),
        enabled = enabled,
        showAllWhenEmpty = true,
    )
}
