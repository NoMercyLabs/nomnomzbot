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

// Web file I/O for the journal export/import (frontend.md §6, same per-target seam style as OAuthLauncher.wasmJs).
//
//   Export → a browser download: wrap the bytes in a Blob, object-URL it, click a transient <a download>. The
//            browser's own Save dialog handles the location; there is no cancel signal, so saveFile reports
//            success once the download is dispatched.
//   Import → the browser file picker: a hidden <input type="file"> whose chosen file is read into a JS staging
//            slot the Kotlin side polls (passing a Kotlin lambda into a js() body is unsupported on wasmJs, so the
//            handshake goes through a small global staging area rather than a callback).
//
// ByteArray <-> JS typed-array conversion is done in tiny external js() shims kept adjacent below.
actual class JournalFileBridge : JournalFileIO {
    actual override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean {
        triggerDownload(suggestedName, bytes.toUint8Array())
        return true
    }

    actual override suspend fun pickFile(): PickedFile? {
        beginFilePick()
        // Poll the JS staging slot until the user picks a file (status "done"), cancels ("cancel"), or the
        // input errors out. The picker is one-shot; polling avoids passing a Kotlin callback into JS.
        while (true) {
            when (pickStatus()) {
                "pending" -> delay(100)
                "done" -> {
                    val name: String = pickedName()
                    val bytes: ByteArray = pickedBytes().toByteArray()
                    clearPick()
                    return PickedFile(name, bytes)
                }
                else -> {
                    // "cancel" or "error" — both surface as no selection.
                    clearPick()
                    return null
                }
            }
        }
    }
}

// ── JS interop shims ─────────────────────────────────────────────────────────

private fun newUint8Array(size: Int): JsAny = js("new Uint8Array(size)")

private fun setByte(array: JsAny, index: Int, value: Int) {
    js("array[index] = value")
}

private fun byteAt(array: JsAny, index: Int): Int = js("array[index]")

private fun lengthOf(array: JsAny): Int = js("array.length")

private fun ByteArray.toUint8Array(): JsAny {
    val array: JsAny = newUint8Array(size)
    for (i in indices) {
        // & 0xFF keeps the value in the unsigned byte range the typed array stores.
        setByte(array, i, this[i].toInt() and 0xFF)
    }
    return array
}

private fun JsAny.toByteArray(): ByteArray {
    val length: Int = lengthOf(this)
    return ByteArray(length) { i -> byteAt(this, i).toByte() }
}

// Create a Blob from the bytes, an object URL for it, click a transient download anchor, then revoke the URL.
private fun triggerDownload(fileName: String, data: JsAny) {
    js(
        """{
            var blob = new Blob([data], { type: 'application/x-ndjson' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }"""
    )
}

// Open a one-shot hidden <input type="file"> and stage its outcome on `globalThis.__nnzJournalPick`, which the
// Kotlin side polls (status: "pending" | "done" | "cancel" | "error").
private fun beginFilePick() {
    js(
        """{
            var slot = { status: 'pending', name: '', bytes: null };
            globalThis.__nnzJournalPick = slot;
            var input = document.createElement('input');
            input.type = 'file';
            input.accept = '.jsonl,.ndjson,application/x-ndjson,application/json,text/plain';
            input.style.display = 'none';
            input.addEventListener('change', function () {
                var file = input.files && input.files[0];
                if (!file) { slot.status = 'cancel'; return; }
                var reader = new FileReader();
                reader.onload = function () {
                    slot.name = file.name;
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

private fun pickStatus(): String = js("(globalThis.__nnzJournalPick ? globalThis.__nnzJournalPick.status : 'cancel')")

private fun pickedName(): String = js("globalThis.__nnzJournalPick.name")

private fun pickedBytes(): JsAny = js("globalThis.__nnzJournalPick.bytes")

private fun clearPick() {
    js("globalThis.__nnzJournalPick = null;")
}
