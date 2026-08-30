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

package bot.nomnomz.dashboard.core.editor

import kotlin.js.ExperimentalWasmJsInterop
import kotlin.js.JsAny
import kotlin.js.JsString
import kotlin.js.Promise
import kotlinx.browser.window
import kotlinx.coroutines.await
import kotlinx.coroutines.channels.Channel
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.w3c.dom.MessageEvent
import org.w3c.dom.events.Event

// Web project editor — a thin bridge to `/editor/index.html`, which is a real page (HTML + CSS + ES modules)
// served by the bot. The editor itself lives there; this file only mounts it, hands it the project, and pumps
// its saves through the caller's `compile`.
//
// Everything the operator sees — the file tree, tabs, Monaco, the problems panel, the esbuild live preview —
// is that page's business. It used to be ~1100 lines of `document.createElement` inside a Kotlin raw string,
// which the Kotlin compiler cannot parse, so every syntax error surfaced only at runtime.
//
// CRITICAL: the iframe mounts into `document.body.shadowRoot || document.body`. Compose/Wasm renders the app
// into a shadow root, and a light-DOM child of a shadow host is NOT laid out — appending to document.body
// leaves the overlay 0x0 and invisible.
private const val EDITOR_PAGE: String = "/editor/index.html"

private const val MESSAGE_READY: String = "nnz:editor:ready"
private const val MESSAGE_OPEN: String = "nnz:editor:open"
private const val MESSAGE_SAVE: String = "nnz:editor:save"
private const val MESSAGE_COMPILED: String = "nnz:editor:compiled"
private const val MESSAGE_CLOSE: String = "nnz:editor:close"

private val editorJson: Json = Json { ignoreUnknownKeys = true }

// What [editorMessageJson] reduces a same-origin editor message to. `files` is populated on save only.
@Serializable
private data class EditorMessage(val type: String, val files: Map<String, String> = emptyMap())

actual class ProjectEditor : ProjectEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        eventSubscriptions: List<String>,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        // Subscribed before the frame exists: the page posts `ready` as soon as its module runs, which can
        // beat any listener installed after the append.
        val inbox: Channel<String> = Channel(Channel.UNLIMITED)
        val listener: (Event) -> Unit = { event: Event ->
            val message: MessageEvent? = event as? MessageEvent
            if (message != null) {
                val envelope: String = editorMessageJson(message.origin, message.data)
                if (envelope.isNotEmpty()) inbox.trySend(envelope)
            }
        }
        window.addEventListener("message", listener)

        val frame: JsAny = mountEditorFrame(EDITOR_PAGE + versionQuery())
        try {
            while (true) {
                val envelope: String = inbox.receiveCatching().getOrNull() ?: return
                val message: EditorMessage =
                    editorJson.decodeFromString(EditorMessage.serializer(), envelope)
                when (message.type) {
                    MESSAGE_READY ->
                        postToEditor(
                            frame,
                            openMessage(title, initialFiles, entryPath, language, sdkTypes, eventSubscriptions),
                        )
                    MESSAGE_SAVE -> {
                        val feedback: CompileFeedback = compile(message.files)
                        postToEditor(frame, compiledMessage(feedback))
                    }
                    MESSAGE_CLOSE -> return
                }
            }
        } finally {
            window.removeEventListener("message", listener)
            removeEditorFrame(frame)
            inbox.close()
        }
    }
}

private fun openMessage(
    title: String,
    files: Map<String, String>,
    entryPath: String,
    language: String,
    sdkTypes: String,
    eventSubscriptions: List<String>,
): String =
    editorJson.encodeToString(
        JsonObject.serializer(),
        buildJsonObject {
            put("type", MESSAGE_OPEN)
            put(
                "payload",
                buildJsonObject {
                    put("title", title)
                    put("files", JsonObject(files.mapValues { entry -> JsonPrimitive(entry.value) }))
                    put("entry", entryPath)
                    put("language", language)
                    put("sdkTypes", sdkTypes)
                    // Per-event payload shapes for the preview's fire bar, so a transient widget can be
                    // triggered without OBS. Single-sourced here rather than duplicated in the page's JS.
                    put("fireSamples", editorJson.parseToJsonElement(WidgetFireBarSamples.allSamplesJson()))
                    // The widget's PERSISTED subscription list — the fire bar's preferred, authoritative source
                    // over scanning source text (see ProjectEditorIO.editAndCompile doc). Empty for a widget
                    // that has not saved any declared subscriptions yet.
                    put(
                        "eventSubscriptions",
                        JsonArray(eventSubscriptions.map { event -> JsonPrimitive(event) }),
                    )
                },
            )
        },
    )

private fun compiledMessage(feedback: CompileFeedback): String =
    editorJson.encodeToString(
        JsonObject.serializer(),
        buildJsonObject {
            put("type", MESSAGE_COMPILED)
            put("ok", feedback.ok)
            put("message", feedback.message)
        },
    )

// The running build, so a deploy invalidates the editor's immutably-cached assets (Program.cs caches any
// `/editor/*` request carrying `v` for a year). There is no build stamp in the wasmJs client — `AppVersion`
// is a jvm-only classpath resource — so this reads the version off the API that served the page, fetched
// once per page. Empty on failure, and the query is then omitted entirely rather than pinned to a made-up
// value: no marker means the server falls back to `no-cache, must-revalidate`, which is always correct.
private suspend fun versionQuery(): String {
    val version: String = runCatching { fetchBuildVersion().await<JsString>().toString() }.getOrDefault("")
    return if (version.isEmpty()) "" else "?v=$version"
}

private fun fetchBuildVersion(): Promise<JsString> =
    js(
        """{
            if (!globalThis.__nnzBuildVersion) {
                globalThis.__nnzBuildVersion = fetch('/health/version', { cache: 'no-store' })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (j) { return (j && typeof j.version === 'string') ? j.version : ''; })
                    .catch(function () { return ''; });
            }
            return globalThis.__nnzBuildVersion;
        }"""
    )

private fun mountEditorFrame(src: String): JsAny =
    js(
        """{
            var frame = document.createElement('iframe');
            frame.setAttribute('data-nnz-project-editor', '');
            frame.setAttribute('title', 'Code editor');
            frame.src = src;
            frame.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;border:none;z-index:2147483000;background:#0a0a0a;';
            (document.body.shadowRoot || document.body).appendChild(frame);
            return frame;
        }"""
    )

private fun removeEditorFrame(frame: JsAny) {
    js("{ if (frame && frame.parentNode) { frame.parentNode.removeChild(frame); } }")
}

private fun postToEditor(frame: JsAny, messageJson: String) {
    js(
        "{ var w = frame.contentWindow; if (w) { w.postMessage(JSON.parse(messageJson), window.location.origin); } }"
    )
}

// Reduce an inbound message to a JSON envelope Kotlin can decode, or '' for anything that is not this
// editor talking: a foreign origin, or any other same-origin postMessage traffic on the page.
private fun editorMessageJson(origin: String, data: JsAny?): String =
    js(
        """{
            if (origin !== window.location.origin) { return ''; }
            var type = data ? data.type : null;
            if (typeof type !== 'string' || type.lastIndexOf('nnz:editor:', 0) !== 0) { return ''; }
            var files = (type === 'nnz:editor:save' && data.files) ? data.files : {};
            return JSON.stringify({ type: type, files: files });
        }"""
    )
