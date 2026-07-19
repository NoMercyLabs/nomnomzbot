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

// Web asset file picker — mirrors the AudioFilePicker.wasmJs.kt handshake:
// a hidden <input type="file" accept="image/*,audio/*"> whose outcome is staged on
// `globalThis.__nnzAssetPick` (its own slot, separate from the audio and journal pickers).
// Kotlin polls until the user picks or cancels.
actual class AssetFilePicker : AssetFilePickerIO {
    actual override suspend fun pick(): AssetFile? {
        beginAssetPick()
        while (true) {
            when (assetPickStatus()) {
                "pending" -> delay(100)
                "done" -> {
                    val name: String = assetPickedName()
                    val mimeType: String = assetPickedMimeType()
                    val bytes: ByteArray = assetPickedBytes().toAssetByteArray()
                    clearAssetPick()
                    return AssetFile(name, mimeType, bytes)
                }
                else -> {
                    clearAssetPick()
                    return null
                }
            }
        }
    }
}

// ── JS interop shims — byte-array bridging identical to AudioFilePicker ──────

private fun assetByteAt(array: JsAny, index: Int): Int = js("array[index]")

private fun assetLengthOf(array: JsAny): Int = js("array.length")

private fun JsAny.toAssetByteArray(): ByteArray {
    val length: Int = assetLengthOf(this)
    return ByteArray(length) { i -> assetByteAt(this, i).toByte() }
}

// Opens a hidden <input type="file"> filtered to images + audio, then stages the result on
// globalThis.__nnzAssetPick. The MIME type is read from File.type (browser-reported); falls back
// to 'application/octet-stream' if absent.
private fun beginAssetPick() {
    js(
        """{
            var slot = { status: 'pending', name: '', mimeType: '', bytes: null };
            globalThis.__nnzAssetPick = slot;
            var input = document.createElement('input');
            input.type = 'file';
            input.accept = 'image/*,audio/*,.png,.jpg,.jpeg,.gif,.webp,.svg,.mp3,.ogg,.wav';
            input.style.display = 'none';
            input.addEventListener('change', function () {
                var file = input.files && input.files[0];
                if (!file) { slot.status = 'cancel'; return; }
                var reader = new FileReader();
                reader.onload = function () {
                    slot.name = file.name;
                    slot.mimeType = file.type || 'application/octet-stream';
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

private fun assetPickStatus(): String =
    js("(globalThis.__nnzAssetPick ? globalThis.__nnzAssetPick.status : 'cancel')")

private fun assetPickedName(): String = js("globalThis.__nnzAssetPick.name")

private fun assetPickedMimeType(): String = js("globalThis.__nnzAssetPick.mimeType")

private fun assetPickedBytes(): JsAny = js("globalThis.__nnzAssetPick.bytes")

private fun clearAssetPick() {
    js("globalThis.__nnzAssetPick = null;")
}
