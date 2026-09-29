// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.state

import bot.nomnomz.dashboard.core.network.WidgetSummary

/**
 * The one catalogue action an overlay row offers for a widget that tracks a catalogue source (a first-party system
 * widget or a gallery install). Both actions pull the catalogue's current source in as a new version; they differ
 * in what the streamer loses sight of, so they are never shown together.
 */
enum class CatalogueRowAction {
    /** A custom widget, or a catalogue widget that is up to date and unedited — nothing to offer. */
    None,

    /** Unedited, and the catalogue moved on: take the update directly (nothing of the streamer's is replaced). */
    Update,

    /**
     * The channel edited the code: "Reset to default" replaces the live code with the catalogue's, so it goes
     * through a confirm that states the consequence first. It also takes any pending catalogue update.
     */
    Reset,
}

/** Which catalogue action this row offers — see [CatalogueRowAction]. */
fun WidgetSummary.catalogueRowAction(): CatalogueRowAction =
    when {
        galleryItemId == null -> CatalogueRowAction.None
        isCustomized -> CatalogueRowAction.Reset
        galleryUpdateAvailable -> CatalogueRowAction.Update
        else -> CatalogueRowAction.None
    }

/** A first-party widget shipped by NomNomzBot itself — the row marks it "System". */
val WidgetSummary.isSystem: Boolean
    get() = source == "first_party"
