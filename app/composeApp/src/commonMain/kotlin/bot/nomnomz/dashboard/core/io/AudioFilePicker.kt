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

// Contract for opening a native OS file picker that filters to audio files and returns the selected
// file's bytes together with its name and detected MIME type. The controller depends on this interface;
// per-platform implementations live in wasmJs and jvm source sets.
interface AudioFilePickerIO {
    /** Prompts the user to pick an audio file. Returns the picked file, or null if cancelled. */
    suspend fun pick(): AudioFile?
}

// The per-target [AudioFilePickerIO] implementation:
//
//   Desktop (jvm):  a native Open dialog filtered to audio extensions; MIME type inferred from extension.
//   Web (wasmJs):   a hidden <input type="file" accept="audio/*"> whose result is staged via a global
//                   slot and polled by the Kotlin side (same handshake as JournalFileBridge).
expect class AudioFilePicker() : AudioFilePickerIO {
    override suspend fun pick(): AudioFile?
}

/** An audio file selected by the user: its display [name], detected [mimeType], and raw [bytes]. */
data class AudioFile(val name: String, val mimeType: String, val bytes: ByteArray) {
    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is AudioFile) return false
        return name == other.name && mimeType == other.mimeType && bytes.contentEquals(other.bytes)
    }

    override fun hashCode(): Int {
        var result: Int = name.hashCode()
        result = 31 * result + mimeType.hashCode()
        result = 31 * result + bytes.contentHashCode()
        return result
    }
}
