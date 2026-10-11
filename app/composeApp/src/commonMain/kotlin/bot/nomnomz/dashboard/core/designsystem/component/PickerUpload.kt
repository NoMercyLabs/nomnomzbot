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

/**
 * The Library uploads a picker can offer inline: a sound clip and a file (asset). Each lambda opens the file
 * dialog, uploads, and returns the new item's id (null when cancelled or failed). A picker takes the one it needs;
 * a null entry hides that picker's upload button.
 */
class PickerUploads(
    val uploadClip: (suspend () -> String?)? = null,
    val uploadAsset: (suspend () -> String?)? = null,
)

/**
 * The one rule every picker's inline upload follows: run [upload]; when it returns the new item's id, hand that id
 * to [select] so the picker shows the new item chosen. A cancelled or failed upload returns null and selects nothing.
 */
suspend fun uploadAndSelect(upload: suspend () -> String?, select: (String) -> Unit) {
    val newId: String = upload() ?: return
    select(newId)
}
