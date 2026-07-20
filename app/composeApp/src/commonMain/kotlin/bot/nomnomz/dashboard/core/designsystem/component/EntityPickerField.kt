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

/**
 * A search dropdown over an ALREADY-LOADED local list of entities — a pipeline, widget, sound clip, quote, code
 * script, etc. This is the "a reference to a row in another table needs a proper search dropdown" primitive:
 * wherever a form field selects one entity out of a channel-owned set, this replaces a raw id text field or a
 * load-everything dropdown with one line, filtering as the user types.
 *
 * It wraps the shared [SearchPickerField] with `showAllWhenEmpty = true`, so the field browses every row on focus
 * (unlike the remote viewer/channel pickers, which require 2 characters so they never enumerate). [idOf]/[labelOf]
 * project each item; the current selection is resolved from [items] by [selectedId], so the caller only ever
 * stores the id. [onSelect] receives the chosen id, or `null` when cleared.
 */
@Composable
fun <T> EntityPickerField(
    items: List<T>,
    selectedId: String?,
    onSelect: (String?) -> Unit,
    idOf: (T) -> String,
    labelOf: (T) -> String,
    modifier: Modifier = Modifier,
    sublabelOf: (T) -> String = { "" },
    label: String? = null,
    placeholder: String? = null,
    emptyText: String? = null,
    enabled: Boolean = true,
) {
    val selectedItem: T? = selectedId?.let { id -> items.firstOrNull { idOf(it) == id } }
    val selectedRef: PickerRef? =
        selectedItem?.let { PickerRef(idOf(it), labelOf(it).ifBlank { idOf(it) }) }

    SearchPickerField(
        search = { query ->
            items
                .filter {
                    query.isBlank() ||
                        labelOf(it).contains(query, ignoreCase = true) ||
                        sublabelOf(it).contains(query, ignoreCase = true) ||
                        idOf(it).contains(query, ignoreCase = true)
                }
                .map { PickerOption(idOf(it), labelOf(it), sublabelOf(it)) }
        },
        selected = selectedRef,
        onSelect = { ref -> onSelect(ref.id) },
        onClear = { onSelect(null) },
        modifier = modifier,
        label = label,
        placeholder = placeholder,
        emptyText = emptyText,
        enabled = enabled,
        showAllWhenEmpty = true,
    )
}
