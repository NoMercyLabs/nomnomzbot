// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.io

// The contract for moving a journal export/import file between the dashboard and the OS. The controller depends on
// THIS (so tests fake it without a real dialog); [JournalFileBridge] is the per-target implementation.
//
// Both methods are user-cancellable: saving returns false when the user cancels; picking returns null. They never
// throw across the seam — a failure surfaces as a cancel/empty result the controller renders as an idle state.
interface JournalFileIO {
    /** Prompts the user for a save location and writes [bytes] there. Returns true on save, false on cancel. */
    suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean

    /** Prompts the user to pick a file and returns its contents, or null when the user cancels. */
    suspend fun pickFile(): PickedFile?
}

// The per-target [JournalFileIO] implementation:
//
//   Desktop (jvm): a native Save dialog writes the exported bytes to disk; a native Open dialog reads a file
//                  the user picks back into memory.
//   Web (wasmJs):  the export triggers a browser download (Blob + anchor); the import opens the browser file
//                  picker (<input type="file">) and reads the chosen file's bytes.
expect class JournalFileBridge() : JournalFileIO {
    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean

    override suspend fun pickFile(): PickedFile?
}

/** A file the user picked for import: its display [name] and raw [bytes]. */
data class PickedFile(val name: String, val bytes: ByteArray) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is PickedFile) return false
        return name == other.name && bytes.contentEquals(other.bytes)
    }

    override fun hashCode(): Int = 31 * name.hashCode() + bytes.contentHashCode()
}
