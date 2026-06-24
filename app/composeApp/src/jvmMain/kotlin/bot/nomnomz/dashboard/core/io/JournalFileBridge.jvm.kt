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

import java.awt.FileDialog
import java.awt.Frame
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

// Desktop file I/O for the journal export/import — native AWT Save/Open dialogs. The dialog and the disk read/
// write are blocking, so both run on Dispatchers.IO (NEVER the Compose UI thread). A cancelled dialog yields a
// null filename, which maps to "save: false" / "pick: null"; an I/O failure is swallowed to the same idle result
// so the seam never throws into the caller.
actual class JournalFileBridge : JournalFileIO {
    actual override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean =
        withContext(Dispatchers.IO) {
            val dialog = FileDialog(null as Frame?, "Export event journal", FileDialog.SAVE)
            dialog.file = suggestedName
            dialog.isVisible = true

            val directory: String? = dialog.directory
            val file: String? = dialog.file
            if (directory == null || file == null) {
                false
            } else {
                try {
                    File(directory, file).writeBytes(bytes)
                    true
                } catch (_: Throwable) {
                    false
                }
            }
        }

    actual override suspend fun pickFile(): PickedFile? =
        withContext(Dispatchers.IO) {
            val dialog = FileDialog(null as Frame?, "Import event journal", FileDialog.LOAD)
            dialog.isVisible = true

            val directory: String? = dialog.directory
            val file: String? = dialog.file
            if (directory == null || file == null) {
                null
            } else {
                try {
                    val handle = File(directory, file)
                    PickedFile(name = handle.name, bytes = handle.readBytes())
                } catch (_: Throwable) {
                    null
                }
            }
        }
}
