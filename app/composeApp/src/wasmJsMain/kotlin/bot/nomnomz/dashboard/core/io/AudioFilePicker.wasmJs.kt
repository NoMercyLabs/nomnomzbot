// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.io

import kotlin.js.ExperimentalWasmJsInterop
import kotlin.js.JsAny
import kotlinx.coroutines.delay

// Web audio file picker — mirrors the JournalFileBridge.wasmJs.kt handshake:
// a hidden <input type="file" accept="audio/*"> whose outcome is staged on `globalThis.__nnzAudioPick`
// (a separate slot from the journal picker). Kotlin polls until the user picks or cancels.
actual class AudioFilePicker : AudioFilePickerIO {
    actual override suspend fun pick(): AudioFile? {
        beginAudioPick()
        while (true) {
            when (audioPickStatus()) {
                "pending" -> delay(100)
                "done" -> {
                    val name: String = audioPickedName()
                    val mimeType: String = audioPickedMimeType()
                    val bytes: ByteArray = audioPickedBytes().toByteArray()
                    clearAudioPick()
                    return AudioFile(name, mimeType, bytes)
                }
                else -> {
                    clearAudioPick()
                    return null
                }
            }
        }
    }
}

// ── JS interop shims — byte-array bridging identical to JournalFileBridge ────

private fun newUint8Array(size: Int): JsAny = js("new Uint8Array(size)")

private fun setByte(array: JsAny, index: Int, value: Int) {
    js("array[index] = value")
}

private fun byteAt(array: JsAny, index: Int): Int = js("array[index]")

private fun lengthOf(array: JsAny): Int = js("array.length")

private fun JsAny.toByteArray(): ByteArray {
    val length: Int = lengthOf(this)
    return ByteArray(length) { i -> byteAt(this, i).toByte() }
}

// Opens a hidden <input type="file"> filtered to audio, then stages the result on globalThis.__nnzAudioPick.
// The MIME type is read directly from File.type (browser-reported); falls back to 'audio/mpeg' if absent.
private fun beginAudioPick() {
    js(
        """{
            var slot = { status: 'pending', name: '', mimeType: '', bytes: null };
            globalThis.__nnzAudioPick = slot;
            var input = document.createElement('input');
            input.type = 'file';
            input.accept = 'audio/*,.mp3,.wav,.ogg,.flac,.aac,.m4a,.opus';
            input.style.display = 'none';
            input.addEventListener('change', function () {
                var file = input.files && input.files[0];
                if (!file) { slot.status = 'cancel'; return; }
                var reader = new FileReader();
                reader.onload = function () {
                    slot.name = file.name;
                    slot.mimeType = file.type || 'audio/mpeg';
                    slot.bytes = new Uint8Array(reader.result);
                    slot.status = 'done';
                };
                reader.onerror = function () { slot.status = 'error'; };
                reader.readAsArrayBuffer(file);
            });
            input.addEventListener('cancel', function () { slot.status = 'cancel'; });
            document.body.appendChild(input);
            input.click();
        }"""
    )
}

private fun audioPickStatus(): String =
    js("(globalThis.__nnzAudioPick ? globalThis.__nnzAudioPick.status : 'cancel')")

private fun audioPickedName(): String = js("globalThis.__nnzAudioPick.name")

private fun audioPickedMimeType(): String = js("globalThis.__nnzAudioPick.mimeType")

private fun audioPickedBytes(): JsAny = js("globalThis.__nnzAudioPick.bytes")

private fun clearAudioPick() {
    js("globalThis.__nnzAudioPick = null;")
}
