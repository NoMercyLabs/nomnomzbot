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
import kotlinx.coroutines.delay
import kotlinx.serialization.builtins.MapSerializer
import kotlinx.serialization.builtins.serializer
import kotlinx.serialization.json.Json

// Web multi-file project editor — a full-screen DOM overlay hosting a file list + tabs + ONE CodeMirror 6 view +
// a live-preview pane, mounted above the Compose canvas. It extends CustomCodeEditor.wasmJs to a whole `src/`
// project:
//   • the file map lives on the JS slot (`globalThis.__nnzProjectEdit.files`, a `path → content` object);
//   • the left sidebar lists the files (add / rename / delete; the entry file is pinned);
//   • ONE CodeMirror view is reused — switching the active file flushes the current doc back into the map and
//     re-creates the view's EditorState with the new file's content + a language matched to the extension;
//   • "Save & Compile" stages the whole map as JSON and Kotlin round-trips it to the caller's compile.
//
// Two enhancements layer on top of that base, both pure conveniences that degrade silently if their esm.sh
// modules can't be reached (the editor stays fully usable without either):
//   1. TypeScript language service — the generated `nnz.d.ts` (passed in as `sdkTypes`) is loaded as an ambient
//      lib into an in-browser TypeScript language service (`@typescript/vfs` + `typescript`, on the MAIN thread —
//      Monaco's Wasm worker is CORS-blocked, and a same-origin worker is not trivially wireable from CDN ESM, so
//      v1 runs the service on-thread). It backs `nnz.` autocompletion (`@codemirror/autocomplete`) + inline
//      red-squiggle diagnostics (`@codemirror/lint`) for the JS/TS-family files. The service extensions live in a
//      CodeMirror Compartment so they can be reconfigured into the live view the moment the service finishes
//      loading and survive every active-file swap.
//   2. esbuild-wasm live preview — a preview pane bundles the CURRENT project client-side with esbuild-wasm
//      (pinned to the server's esbuild 0.28.1) over an in-memory virtual file system and renders the bundle in a
//      sandboxed `<iframe>` that hot-reloads on edit (debounced). This is a DEV-LOOP convenience ONLY — the
//      server rebuild on Save & Compile stays the trust boundary, unchanged. Bare imports are left external and
//      resolved in the iframe by an import map (react / react-dom / vue), so vanilla + react projects preview
//      end-to-end. Vue SFCs (`.vue`) need @vue/compiler-sfc's non-trivial @vue/repl-style assembly to match the
//      server exactly, so their preview is scoped to the server build (a clear note in the pane); code-scripts
//      have no DOM to render, so their pane shows a "validate with Save & Compile" note while autocomplete stays
//      live.
//
// Handshake (same global-slot polling as CustomCodeEditor): JS stages { status, pendingFilesJson, ... } and Kotlin
// polls it. `compile` → JS stages the full file JSON; Kotlin flips 'busy', awaits the caller's compile, paints the
// result, flips back to 'editing'. 'closed' → Kotlin returns. CodeMirror + the two enhancements load from a CDN via
// a Function()-hidden dynamic import (webpack rejects a literal import()); a <textarea> fallback keeps the editor
// usable offline.
//
// CRITICAL: the overlay mounts into `document.body.shadowRoot || document.body`. Compose/Wasm renders the app into
// a shadow root, and a light-DOM child of a shadow host is NOT laid out — appending to document.body would leave
// the overlay 0×0 and invisible. The preview iframe is a child of the overlay, so it inherits that shadow-root
// mount automatically.
private val filesJson: Json = Json { encodeDefaults = true }
private val filesSerializer = MapSerializer(String.serializer(), String.serializer())

actual class ProjectEditor : ProjectEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        sdkTypes: String,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        openProjectEditor(title, filesJson.encodeToString(filesSerializer, initialFiles), entryPath, language, sdkTypes)
        try {
            while (true) {
                when (projectEditorStatus()) {
                    "editing", "busy" -> delay(80)
                    "compile" -> {
                        val payload: String = projectEditorFilesJson()
                        val files: Map<String, String> =
                            try {
                                filesJson.decodeFromString(filesSerializer, payload)
                            } catch (_: Throwable) {
                                emptyMap()
                            }
                        beginCompile()
                        val feedback: CompileFeedback = compile(files)
                        reportCompile(feedback.ok, feedback.message)
                    }
                    else -> return
                }
            }
        } finally {
            closeProjectEditor()
        }
    }
}

private fun projectEditorStatus(): String =
    js("(globalThis.__nnzProjectEdit ? globalThis.__nnzProjectEdit.status : 'closed')")

// The full file map staged by the JS side (JSON string) at the moment "Save & Compile" was pressed.
private fun projectEditorFilesJson(): String =
    js(
        "(globalThis.__nnzProjectEdit && typeof globalThis.__nnzProjectEdit.pendingFilesJson === 'string') ? globalThis.__nnzProjectEdit.pendingFilesJson : '{}'"
    )

// Guard: flip out of 'compile' so the poll loop does not read the same save twice while the build runs.
private fun beginCompile() {
    js("{ var s = globalThis.__nnzProjectEdit; if (s) { s.status = 'busy'; } }")
}

// Paint the build result inline, re-enable the save button, and return the editor to the editable state.
private fun reportCompile(ok: Boolean, message: String) {
    js(
        """{
            var s = globalThis.__nnzProjectEdit;
            if (!s) { return; }
            if (s.result) {
                s.result.textContent = message;
                s.result.style.display = 'block';
                s.result.style.color = ok ? '#4ade80' : '#f87171';
                s.result.style.background = ok ? 'rgba(34,197,94,0.12)' : 'rgba(239,68,68,0.12)';
            }
            if (s.saveBtn) { s.saveBtn.disabled = false; s.saveBtn.textContent = 'Save & Compile'; }
            s.status = 'editing';
        }"""
    )
}

// Tears the overlay out of the DOM and clears the slot (removing the element disposes its CodeMirror view).
private fun closeProjectEditor() {
    js(
        "{ var s = globalThis.__nnzProjectEdit; if (s) { if (s.previewTimer) { clearTimeout(s.previewTimer); } if (s.el && s.el.parentNode) { s.el.parentNode.removeChild(s.el); } } globalThis.__nnzProjectEdit = null; }"
    )
}

// Builds the full-screen multi-file editor overlay and stages its state on globalThis.__nnzProjectEdit.
// `title`, `initialFilesJson`, `entryPath`, `language`, and `sdkTypes` are the enclosing function's parameters —
// referenced directly in the JS body (Kotlin/Wasm js() marshals them as real JS values, so there is no
// string-injection surface). NOTE: the JS body must not contain a literal `$` (Kotlin string templates), so all
// string building uses `+` concatenation and identifier-only regexes.
private fun openProjectEditor(
    title: String,
    initialFilesJson: String,
    entryPath: String,
    language: String,
    sdkTypes: String,
) {
    js(
        """{
            var files = {};
            try { files = JSON.parse(initialFilesJson) || {}; } catch (e) { files = {}; }
            var paths = Object.keys(files);
            var entry = (entryPath && (entryPath in files)) ? entryPath : (paths.length ? paths[0] : entryPath);
            if (!(entry in files)) { files[entry] = ''; }

            var slot = {
                status: 'editing', files: files, entry: entry, active: entry, pendingFilesJson: '',
                el: null, host: null, view: null, textarea: null, result: null, saveBtn: null,
                fileListEl: null, tabsEl: null,
                CmView: null, CmState: null, langHtml: null, langJs: null, baseExt: null,
                tsCompartment: null, tsReady: false, tsExtensions: null, tsEnv: null, tsLib: null,
                previewFrame: null, previewNote: null, previewMode: 'esbuild', previewNoteText: '',
                esbuild: null, previewTimer: null, vue: false, vueSfc: null, fireBar: null
            };
            globalThis.__nnzProjectEdit = slot;

            var overlay = document.createElement('div');
            overlay.setAttribute('data-nnz-project-editor', '');
            overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483000;display:flex;flex-direction:column;background:#0a0a0a;color:#e5e5e5;font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif;';
            slot.el = overlay;

            var header = document.createElement('div');
            header.style.cssText = 'display:flex;align-items:center;gap:12px;padding:10px 16px;border-bottom:1px solid #262626;background:#141414;';

            var titleEl = document.createElement('div');
            titleEl.textContent = title;
            titleEl.style.cssText = 'font-size:14px;font-weight:600;flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';

            var langBadge = document.createElement('div');
            langBadge.textContent = (language || '').toUpperCase();
            langBadge.style.cssText = 'font-size:11px;color:#a3a3a3;padding:2px 8px;border:1px solid #333;border-radius:6px;';

            var hint = document.createElement('div');
            hint.textContent = 'Esc to close · Ctrl+S to save & compile';
            hint.style.cssText = 'font-size:11px;color:#666;white-space:nowrap;';

            var closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.textContent = 'Close';
            closeBtn.style.cssText = 'padding:7px 16px;font-size:13px;font-weight:600;color:#e5e5e5;background:transparent;border:1px solid #333;border-radius:8px;cursor:pointer;';

            var saveBtn = document.createElement('button');
            saveBtn.type = 'button';
            saveBtn.textContent = 'Save & Compile';
            saveBtn.style.cssText = 'padding:7px 16px;font-size:13px;font-weight:600;color:#ffffff;background:#772ce8;border:none;border-radius:8px;cursor:pointer;';
            slot.saveBtn = saveBtn;

            header.appendChild(titleEl);
            header.appendChild(langBadge);
            header.appendChild(hint);
            header.appendChild(closeBtn);
            header.appendChild(saveBtn);

            var result = document.createElement('div');
            result.style.cssText = 'display:none;padding:8px 16px;font-size:13px;font-weight:600;border-bottom:1px solid #262626;';
            slot.result = result;

            // Body: a fixed-width file sidebar + the main editor column (tabs strip over the CodeMirror host) + a
            // live-preview column on the right.
            var body = document.createElement('div');
            body.style.cssText = 'flex:1;min-height:0;display:flex;';

            var sidebar = document.createElement('div');
            sidebar.style.cssText = 'width:240px;min-width:200px;display:flex;flex-direction:column;border-right:1px solid #262626;background:#0f0f0f;';

            var sidebarHead = document.createElement('div');
            sidebarHead.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:8px 12px;border-bottom:1px solid #1f1f1f;';
            var sidebarTitle = document.createElement('div');
            sidebarTitle.textContent = 'src';
            sidebarTitle.style.cssText = 'font-size:11px;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;color:#a3a3a3;';
            var addBtn = document.createElement('button');
            addBtn.type = 'button';
            addBtn.textContent = '+ New file';
            addBtn.style.cssText = 'font-size:11px;color:#c4b5fd;background:transparent;border:1px solid #333;border-radius:6px;padding:3px 8px;cursor:pointer;';
            sidebarHead.appendChild(sidebarTitle);
            sidebarHead.appendChild(addBtn);

            var fileListEl = document.createElement('div');
            fileListEl.style.cssText = 'flex:1;min-height:0;overflow:auto;padding:6px;';
            slot.fileListEl = fileListEl;

            sidebar.appendChild(sidebarHead);
            sidebar.appendChild(fileListEl);

            var mainCol = document.createElement('div');
            mainCol.style.cssText = 'flex:1;min-width:0;display:flex;flex-direction:column;';

            var tabsEl = document.createElement('div');
            tabsEl.style.cssText = 'display:flex;gap:2px;padding:6px 8px;border-bottom:1px solid #262626;background:#111;overflow-x:auto;';
            slot.tabsEl = tabsEl;

            var host = document.createElement('div');
            host.style.cssText = 'flex:1;min-height:0;overflow:hidden;position:relative;';
            slot.host = host;

            mainCol.appendChild(tabsEl);
            mainCol.appendChild(host);

            // Preview column.
            var previewCol = document.createElement('div');
            previewCol.style.cssText = 'width:42%;min-width:300px;display:flex;flex-direction:column;border-left:1px solid #262626;background:#111;';

            var previewHead = document.createElement('div');
            previewHead.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:8px 12px;border-bottom:1px solid #1f1f1f;';
            var previewTitle = document.createElement('div');
            previewTitle.textContent = 'Live preview';
            previewTitle.style.cssText = 'font-size:11px;font-weight:600;letter-spacing:0.04em;text-transform:uppercase;color:#a3a3a3;';
            var refreshBtn = document.createElement('button');
            refreshBtn.type = 'button';
            refreshBtn.textContent = 'Refresh';
            refreshBtn.style.cssText = 'font-size:11px;color:#c4b5fd;background:transparent;border:1px solid #333;border-radius:6px;padding:3px 8px;cursor:pointer;';
            previewHead.appendChild(previewTitle);
            previewHead.appendChild(refreshBtn);

            // Fire bar: one button per event the widget listens for (scanned from the source), so transient
            // widgets (alerts/BSOD) can be triggered in the preview without OBS. Populated after each build.
            var fireBar = document.createElement('div');
            fireBar.style.cssText = 'display:none;flex-wrap:wrap;gap:6px;padding:8px 12px;border-bottom:1px solid #1f1f1f;background:#0f0f0f;';
            slot.fireBar = fireBar;

            var previewBody = document.createElement('div');
            previewBody.style.cssText = 'flex:1;min-height:0;position:relative;background:#0a0a0a;';

            var previewNote = document.createElement('div');
            previewNote.style.cssText = 'position:absolute;inset:0;display:none;align-items:center;justify-content:center;text-align:center;padding:24px;font-size:13px;line-height:1.5;color:#a3a3a3;';
            slot.previewNote = previewNote;

            var previewFrame = document.createElement('iframe');
            previewFrame.setAttribute('sandbox', 'allow-scripts');
            previewFrame.setAttribute('title', 'Widget preview');
            previewFrame.style.cssText = 'width:100%;height:100%;border:none;background:transparent;display:none;';
            slot.previewFrame = previewFrame;

            previewBody.appendChild(previewFrame);
            previewBody.appendChild(previewNote);
            previewCol.appendChild(previewHead);
            previewCol.appendChild(fireBar);
            previewCol.appendChild(previewBody);

            body.appendChild(sidebar);
            body.appendChild(mainCol);
            body.appendChild(previewCol);

            overlay.appendChild(header);
            overlay.appendChild(result);
            overlay.appendChild(body);

            var mountRoot = document.body.shadowRoot || document.body;
            mountRoot.appendChild(overlay);

            // ── File-content plumbing ────────────────────────────────────────────
            function flushActive() {
                if (slot.view) { slot.files[slot.active] = slot.view.state.doc.toString(); }
                else if (slot.textarea) { slot.files[slot.active] = slot.textarea.value; }
            }
            function snapshotFiles() {
                flushActive();
                var out = {};
                for (var k in slot.files) { if (Object.prototype.hasOwnProperty.call(slot.files, k)) { out[k] = slot.files[k]; } }
                return out;
            }
            function extOf(path) { return (path.split('.').pop() || '').toLowerCase(); }
            function isJsFamily(path) {
                var e = extOf(path);
                return e === 'js' || e === 'jsx' || e === 'ts' || e === 'tsx' || e === 'mjs' || e === 'cjs';
            }
            function langExtFor(path) {
                if (isJsFamily(path)) { return slot.langJs ? [slot.langJs()] : []; }
                return slot.langHtml ? [slot.langHtml()] : [];
            }
            // The TypeScript language-service extensions for a file — only the JS/TS family once the service is up.
            function tsExtFor(path) {
                if (slot.tsReady && slot.tsExtensions && isJsFamily(path)) { return slot.tsExtensions; }
                return [];
            }
            function extsFor(path) {
                var arr = slot.baseExt.concat(langExtFor(path));
                if (slot.tsCompartment) { arr = arr.concat([slot.tsCompartment.of(tsExtFor(path))]); }
                return arr;
            }
            function applyDoc(path) {
                if (slot.view && slot.CmState && slot.baseExt) {
                    var state = slot.CmState.create({ doc: slot.files[path] || '', extensions: extsFor(path) });
                    slot.view.setState(state);
                    slot.view.focus();
                } else if (slot.textarea) {
                    slot.textarea.value = slot.files[path] || '';
                    slot.textarea.focus();
                }
            }
            function switchTo(path) {
                if (!(path in slot.files)) { return; }
                flushActive();
                slot.active = path;
                applyDoc(path);
                renderFiles();
                renderTabs();
            }
            slot.switchTo = switchTo;

            function normalizePath(p) {
                // Trim leading/trailing/duplicate slashes without a regex (avoids a literal end-anchor in the js literal).
                var parts = String(p).split('/');
                var kept = [];
                for (var i = 0; i < parts.length; i++) { if (parts[i].length) { kept.push(parts[i]); } }
                return kept.join('/').trim();
            }
            function addFile() {
                var name = (typeof prompt === 'function') ? prompt('New file path (e.g. components/Bar.vue)') : null;
                if (!name) { return; }
                name = normalizePath(name);
                if (!name || (name in slot.files)) { return; }
                slot.files[name] = '';
                switchTo(name);
            }
            function renameFile(path) {
                if (path === slot.entry) { return; }
                var next = (typeof prompt === 'function') ? prompt('Rename file', path) : null;
                if (!next) { return; }
                next = normalizePath(next);
                if (!next || next === path || (next in slot.files)) { return; }
                flushActive();
                slot.files[next] = slot.files[path];
                delete slot.files[path];
                if (slot.active === path) { slot.active = next; }
                applyDoc(slot.active);
                renderFiles();
                renderTabs();
                schedulePreview();
            }
            function deleteFile(path) {
                if (path === slot.entry) { return; }
                delete slot.files[path];
                if (slot.active === path) { slot.active = slot.entry; }
                applyDoc(slot.active);
                renderFiles();
                renderTabs();
                schedulePreview();
            }

            function renderFiles() {
                fileListEl.innerHTML = '';
                var names = Object.keys(slot.files).sort();
                for (var i = 0; i < names.length; i++) {
                    (function (path) {
                        var isActive = path === slot.active;
                        var row = document.createElement('div');
                        row.style.cssText = 'display:flex;align-items:center;gap:6px;padding:5px 8px;border-radius:6px;cursor:pointer;font-size:13px;' +
                            (isActive ? 'background:#26262b;color:#fff;' : 'color:#cfcfcf;');
                        var label = document.createElement('span');
                        label.textContent = path + (path === slot.entry ? '  •' : '');
                        label.style.cssText = 'flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;';
                        label.addEventListener('click', function () { switchTo(path); });
                        row.appendChild(label);
                        if (path !== slot.entry) {
                            var ren = document.createElement('button');
                            ren.type = 'button'; ren.textContent = '✎';
                            ren.title = 'Rename';
                            ren.style.cssText = 'background:transparent;border:none;color:#8a8a8a;cursor:pointer;font-size:12px;';
                            ren.addEventListener('click', function (e) { e.stopPropagation(); renameFile(path); });
                            row.appendChild(ren);
                            var del = document.createElement('button');
                            del.type = 'button'; del.textContent = '✕';
                            del.title = 'Delete';
                            del.style.cssText = 'background:transparent;border:none;color:#8a8a8a;cursor:pointer;font-size:12px;';
                            del.addEventListener('click', function (e) { e.stopPropagation(); deleteFile(path); });
                            row.appendChild(del);
                        }
                        fileListEl.appendChild(row);
                    })(names[i]);
                }
            }

            function renderTabs() {
                tabsEl.innerHTML = '';
                var names = Object.keys(slot.files).sort();
                for (var i = 0; i < names.length; i++) {
                    (function (path) {
                        var isActive = path === slot.active;
                        var tab = document.createElement('button');
                        tab.type = 'button';
                        tab.textContent = path.split('/').pop();
                        tab.title = path;
                        tab.style.cssText = 'font-size:12px;padding:5px 10px;border-radius:6px 6px 0 0;cursor:pointer;border:1px solid ' +
                            (isActive ? '#333;background:#0a0a0a;color:#fff;border-bottom-color:#0a0a0a;' : 'transparent;background:transparent;color:#9a9a9a;');
                        tab.addEventListener('click', function () { switchTo(path); });
                        tabsEl.appendChild(tab);
                    })(names[i]);
                }
            }

            function currentFilesJson() {
                flushActive();
                return JSON.stringify(slot.files);
            }
            function doSave() {
                if (slot.status !== 'editing') { return; }
                slot.pendingFilesJson = currentFilesJson();
                slot.status = 'compile';
                saveBtn.disabled = true;
                saveBtn.textContent = 'Compiling…';
                result.style.display = 'none';
            }
            function doClose() { slot.status = 'closed'; }

            addBtn.addEventListener('click', addFile);
            saveBtn.addEventListener('click', doSave);
            closeBtn.addEventListener('click', doClose);
            refreshBtn.addEventListener('click', function () { rebuildPreviewNow(); });
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); doClose(); }
                else if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) { e.preventDefault(); doSave(); }
            });

            // ── Live preview ─────────────────────────────────────────────────────
            function showPreviewNote(text, isError) {
                slot.previewFrame.style.display = 'none';
                if (slot.fireBar) { slot.fireBar.style.display = 'none'; }
                slot.previewNote.textContent = text;
                slot.previewNote.style.color = isError ? '#f87171' : '#a3a3a3';
                slot.previewNote.style.display = 'flex';
            }
            function importMapJson() {
                return JSON.stringify({ imports: {
                    'react': 'https://esm.sh/react@18',
                    'react-dom': 'https://esm.sh/react-dom@18',
                    'react-dom/client': 'https://esm.sh/react-dom@18/client',
                    'react/jsx-runtime': 'https://esm.sh/react@18/jsx-runtime',
                    'react/jsx-dev-runtime': 'https://esm.sh/react@18/jsx-dev-runtime',
                    'vue': 'https://esm.sh/vue@3.5.39'
                } });
            }
            // A socket-free stand-in for the overlay SDK (window.NomNomz) so a widget mounts + subscribes without a
            // hub. Same surface as /overlay/sdk.js (on/off/onAny/onSettings/settings/reportError); a postMessage
            // bridge lets the fire bar drive events and push settings. Injected as a classic script so it exists
            // before the deferred module bundle runs.
            function previewSdkStub() {
                return '(function(){' +
                    'var handlers={},anyH=[],setH=[],settings=(window.WIDGET_SETTINGS&&typeof window.WIDGET_SETTINGS===\"object\")?window.WIDGET_SETTINGS:{};' +
                    'function on(t,f){if(typeof f===\"function\")(handlers[t]=handlers[t]||[]).push(f);return api;}' +
                    'function off(t,f){var l=handlers[t];if(l)handlers[t]=l.filter(function(h){return h!==f;});return api;}' +
                    'function onAny(f){if(typeof f===\"function\")anyH.push(f);return api;}' +
                    'function onSettings(f){if(typeof f===\"function\"){setH.push(f);try{f(settings);}catch(e){}}return api;}' +
                    'function emit(t,d){(handlers[t]||[]).forEach(function(f){try{f(d,t);}catch(e){console.error(e);}});anyH.forEach(function(f){try{f(t,d);}catch(e){}});}' +
                    'var api={on:on,off:off,onAny:onAny,onSettings:onSettings,reportError:function(m){console.error(\"[preview widget]\",m);},get settings(){return settings;}};' +
                    'window.NomNomz=api;' +
                    'window.addEventListener(\"message\",function(ev){var m=ev.data;if(!m)return;if(m.__nnzFire){emit(m.__nnzFire.type,m.__nnzFire.data||{});}else if(m.__nnzSettings){settings=m.__nnzSettings;setH.forEach(function(f){try{f(settings);}catch(e){}});}});' +
                    '})();';
            }
            function renderPreviewBundle(jsCode, cssCode) {
                var reset = 'html,body{margin:0;padding:0;background:transparent;color:#e5e5e5;font-family:-apple-system,BlinkMacSystemFont,sans-serif;}';
                var sdk = slot.vue ? ('<script>' + previewSdkStub() + '<\/script>') : '';
                var doc = '<!doctype html><html><head><meta charset=\"utf-8\">' +
                    '<style>' + reset + '</style><style>' + (cssCode || '') + '</style>' +
                    '<script type=\"importmap\">' + importMapJson() + '<\/script></head><body>' +
                    '<div id=\"app\"></div><div id=\"root\"></div>' + sdk +
                    '<script type=\"module\">' + jsCode + '<\/script></body></html>';
                slot.previewNote.style.display = 'none';
                slot.previewFrame.style.display = 'block';
                slot.previewFrame.srcdoc = doc;
                refreshFireBar();
            }
            // Scan every file for the events the widget subscribes to — nnz.on('evt') / NomNomz.on(\"evt\") — and
            // render one fire button each. Clicking posts the event into the sandboxed preview iframe.
            function refreshFireBar() {
                if (!slot.fireBar) { return; }
                if (!slot.vue) { slot.fireBar.style.display = 'none'; return; }
                var seen = {}; var events = [];
                var re = /\.on\(\s*['\"]([a-zA-Z0-9_.:-]+)['\"]/g;
                var all = snapshotFiles();
                for (var p in all) {
                    if (!Object.prototype.hasOwnProperty.call(all, p)) { continue; }
                    var src = all[p]; var mm;
                    while ((mm = re.exec(src)) !== null) {
                        var ev = mm[1];
                        if (ev && ev !== 'message' && ev !== 'error' && !seen[ev]) { seen[ev] = true; events.push(ev); }
                    }
                }
                slot.fireBar.innerHTML = '';
                if (!events.length) { slot.fireBar.style.display = 'none'; return; }
                var lbl = document.createElement('span');
                lbl.textContent = 'Fire event:';
                lbl.style.cssText = 'font-size:11px;color:#a3a3a3;align-self:center;margin-right:2px;';
                slot.fireBar.appendChild(lbl);
                for (var i = 0; i < events.length; i++) {
                    (function (ev) {
                        var b = document.createElement('button');
                        b.type = 'button'; b.textContent = ev;
                        b.style.cssText = 'font-size:11px;color:#e5e5e5;background:#1e1e22;border:1px solid #333;border-radius:6px;padding:3px 8px;cursor:pointer;';
                        b.addEventListener('click', function () {
                            if (slot.previewFrame && slot.previewFrame.contentWindow) {
                                slot.previewFrame.contentWindow.postMessage({ __nnzFire: { type: ev, data: {} } }, '*');
                            }
                        });
                        slot.fireBar.appendChild(b);
                    })(events[i]);
                }
                slot.fireBar.style.display = 'flex';
            }
            function renderHtmlDirect() {
                var f = snapshotFiles();
                slot.previewNote.style.display = 'none';
                slot.previewFrame.style.display = 'block';
                slot.previewFrame.srcdoc = f[slot.entry] || '';
            }
            function extLoader(path) {
                var e = extOf(path);
                if (e === 'ts') { return 'ts'; }
                if (e === 'tsx') { return 'tsx'; }
                if (e === 'jsx') { return 'jsx'; }
                if (e === 'json') { return 'json'; }
                if (e === 'css') { return 'css'; }
                return 'js';
            }
            function resolveVfs(importer, spec) {
                var baseDir = importer.indexOf('/') >= 0 ? importer.slice(0, importer.lastIndexOf('/')) : '';
                var parts = baseDir ? baseDir.split('/') : [];
                var segs = spec.split('/');
                for (var i = 0; i < segs.length; i++) {
                    var s = segs[i];
                    if (s === '.' || s === '') { continue; }
                    if (s === '..') { parts.pop(); continue; }
                    parts.push(s);
                }
                var base = parts.join('/');
                var cands = [base, base + '.js', base + '.ts', base + '.jsx', base + '.tsx', base + '.mjs',
                    base + '.vue', base + '.json', base + '/index.js', base + '/index.ts', base + '/index.jsx',
                    base + '/index.tsx', base + '/index.vue'];
                for (var j = 0; j < cands.length; j++) { if (cands[j] in slot.files) { return cands[j]; } }
                return null;
            }
            // Synthetic entry for Vue projects: the entry SFC exports a component but mounts nothing, so we bundle
            // from a generated root that imports it and mounts with a fresh createApp — matching the overlay runtime.
            var VUE_ENTRY = '__nnz_vue_main__.js';
            function vueEntrySource() {
                return 'import __App from "./' + slot.entry + '";\n' +
                    'import { createApp } from "vue";\n' +
                    'try { window.__nnzApp = createApp(__App); window.__nnzApp.mount("#app"); }\n' +
                    'catch (e) { var d = document.getElementById("app"); if (d) { d.textContent = "Mount error: " + ((e && e.message) || e); d.style.color = "#f87171"; } }';
            }
            // Compile one Vue SFC to an ES module the same way the server does (@vue/compiler-sfc): <script setup>
            // with the template inlined, a stable scope id, and scoped <style> injected at runtime. The output keeps
            // TS syntax (esbuild strips it via the 'ts' loader on the caller side).
            function compileVueFile(path, source) {
                var sfc = slot.vueSfc;
                var parsed = sfc.parse(source, { filename: path });
                if (parsed.errors && parsed.errors.length) { throw new Error(parsed.errors[0].message || String(parsed.errors[0])); }
                var descriptor = parsed.descriptor;
                var h = 0; for (var i = 0; i < path.length; i++) { h = ((h << 5) - h + path.charCodeAt(i)) | 0; }
                var id = Math.abs(h).toString(36);
                var scoped = descriptor.styles.some(function (s) { return s.scoped; });
                if (!descriptor.scriptSetup && !descriptor.script) { throw new Error('SFC has no <script> block'); }
                var cs = sfc.compileScript(descriptor, { id: id, inlineTemplate: true, templateOptions: { scoped: scoped }, babelParserPlugins: ['typescript'] });
                // rewriteDefault re-parses the compiled script (still TS) — it needs the typescript plugin too.
                var code = sfc.rewriteDefault(cs.content, '__sfc_main', ['typescript']);
                if (scoped) { code += '\n__sfc_main.__scopeId = "data-v-' + id + '";'; }
                var css = '';
                for (var j = 0; j < descriptor.styles.length; j++) {
                    var stel = descriptor.styles[j];
                    var out = sfc.compileStyle({ source: stel.content, filename: path, id: id, scoped: stel.scoped });
                    css += out.code;
                }
                if (css) { code += '\n;(function(){var __st=document.createElement("style");__st.textContent=' + JSON.stringify(css) + ';document.head.appendChild(__st);})();'; }
                code += '\nexport default __sfc_main;';
                return code;
            }
            function vfsPlugin() {
                return { name: 'nnz-vfs', setup: function (build) {
                    build.onResolve({ filter: /.*/ }, function (a) {
                        if (a.kind === 'entry-point') { return { path: a.path, namespace: 'nnzvfs' }; }
                        if (a.path.charAt(0) === '.') {
                            var r = resolveVfs(a.importer, a.path);
                            if (r) { return { path: r, namespace: 'nnzvfs' }; }
                            return { errors: [{ text: 'Cannot resolve ' + a.path + ' from ' + a.importer }] };
                        }
                        // Bare specifier — leave external so the iframe import map resolves it (react / vue / …).
                        return { path: a.path, external: true };
                    });
                    build.onLoad({ filter: /.*/, namespace: 'nnzvfs' }, function (a) {
                        if (a.path === VUE_ENTRY) { return { contents: vueEntrySource(), loader: 'js' }; }
                        var c = slot.files[a.path];
                        if (c == null) { return { errors: [{ text: 'Missing file ' + a.path }] }; }
                        if (a.path.slice(-4) === '.vue') {
                            try { return { contents: compileVueFile(a.path, c), loader: 'ts' }; }
                            catch (e) { return { errors: [{ text: 'Vue compile (' + a.path + '): ' + ((e && e.message) || e) }] }; }
                        }
                        return { contents: c, loader: extLoader(a.path) };
                    });
                } };
            }
            function buildEsbuildPreview() {
                if (!slot.esbuild) { return; }
                var f = snapshotFiles();
                if (!(slot.entry in f)) { showPreviewNote('Entry file ' + slot.entry + ' is missing.', true); return; }
                // Vue projects need @vue/compiler-sfc — load it once, then rebuild.
                if (slot.vue && !slot.vueSfc) {
                    showPreviewNote('Loading Vue compiler…', false);
                    var diVue = new Function('u', 'return import(u);');
                    diVue('https://esm.sh/@vue/compiler-sfc@3.5.39?deps=vue@3.5.39').then(function (m) {
                        var mod = (m && m.parse) ? m : (m && m.default ? m.default : m);
                        slot.vueSfc = mod;
                        buildEsbuildPreview();
                    }).catch(function (err) {
                        showPreviewNote('Vue compiler could not load:\n' + ((err && err.message) || err), true);
                    });
                    return;
                }
                var entryPoint = slot.vue ? VUE_ENTRY : slot.entry;
                slot.esbuild.build({
                    entryPoints: [entryPoint], bundle: true, write: false, format: 'esm', target: 'es2020',
                    outdir: 'nnzout', jsx: 'automatic', jsxImportSource: 'react',
                    loader: { '.png': 'dataurl', '.jpg': 'dataurl', '.jpeg': 'dataurl', '.gif': 'dataurl',
                        '.svg': 'text', '.woff': 'dataurl', '.woff2': 'dataurl' },
                    plugins: [vfsPlugin()]
                }).then(function (res) {
                    var jsOut = '', cssOut = '';
                    for (var i = 0; i < res.outputFiles.length; i++) {
                        var of = res.outputFiles[i];
                        if (of.path.slice(-3) === '.js') { jsOut = of.text; }
                        else if (of.path.slice(-4) === '.css') { cssOut = of.text; }
                    }
                    renderPreviewBundle(jsOut, cssOut);
                }).catch(function (err) {
                    var msg = (err && err.message) ? err.message : String(err);
                    showPreviewNote('Preview build failed:\n' + msg, true);
                });
            }
            function rebuildPreviewNow() {
                if (slot.previewMode === 'esbuild') { buildEsbuildPreview(); }
                else if (slot.previewMode === 'html') { renderHtmlDirect(); }
                else { showPreviewNote(slot.previewNoteText, false); }
            }
            function schedulePreview() {
                if (slot.previewMode !== 'esbuild' && slot.previewMode !== 'html') { return; }
                if (slot.previewTimer) { clearTimeout(slot.previewTimer); }
                slot.previewTimer = setTimeout(rebuildPreviewNow, 500);
            }
            slot.schedulePreview = schedulePreview;

            // Decide the preview strategy from the framework + entry extension.
            (function () {
                var fw = (language || '').toLowerCase();
                var ee = extOf(slot.entry);
                if (fw === 'script') {
                    slot.previewMode = 'note';
                    slot.previewNoteText = 'Code scripts run in the bot sandbox — press Save & Compile to validate. Autocomplete + inline errors are live in the editor.';
                } else if (fw === 'vue') {
                    // Vue SFCs compile client-side with @vue/compiler-sfc (same as the server) and bundle through
                    // the same esbuild path as vanilla/react — a live, hot-reloading preview that mounts the widget
                    // with a socket-free NomNomz SDK stub. The fire bar (built once the source is scanned) drives
                    // the widget's events so transient widgets (alerts/BSOD) can be seen reacting without OBS.
                    slot.vue = true;
                    slot.previewMode = 'esbuild';
                } else if (ee === 'html' || ee === 'htm') {
                    slot.previewMode = 'html';
                } else {
                    slot.previewMode = 'esbuild';
                }
            })();

            function mountTextarea() {
                if (slot.textarea || slot.view) { return; }
                var ta = document.createElement('textarea');
                ta.value = slot.files[slot.active] || '';
                ta.spellcheck = false;
                ta.wrap = 'off';
                ta.style.cssText = 'width:100%;height:100%;box-sizing:border-box;resize:none;border:none;outline:none;padding:14px;background:#0a0a0a;color:#e5e5e5;font-family:\"Cascadia Code\",\"Fira Code\",Menlo,Consolas,monospace;font-size:13px;line-height:1.5;tab-size:2;white-space:pre;overflow:auto;';
                ta.addEventListener('keydown', function (e) {
                    if (e.key === 'Tab') {
                        e.preventDefault();
                        var a = ta.selectionStart, b = ta.selectionEnd;
                        ta.value = ta.value.substring(0, a) + '  ' + ta.value.substring(b);
                        ta.selectionStart = ta.selectionEnd = a + 2;
                    }
                });
                ta.addEventListener('input', function () { schedulePreview(); });
                host.appendChild(ta);
                slot.textarea = ta;
                ta.focus();
            }

            renderFiles();
            renderTabs();

            // ── TypeScript language service (autocomplete + diagnostics over nnz.d.ts) ───────────────────────────
            // Runs on the MAIN thread with @typescript/vfs. One virtual file '/main.tsx' mirrors the active JS/TS
            // doc; '/nnz.d.ts' is loaded as an ambient lib so the global `declare const nnz` is in scope. Any load
            // failure is swallowed — the editor keeps working with no autocomplete.
            function tsKindToCmType(kind) {
                if (kind === 'method' || kind === 'function') { return 'function'; }
                if (kind === 'property' || kind === 'getter' || kind === 'setter') { return 'property'; }
                if (kind === 'const' || kind === 'let' || kind === 'var' || kind === 'parameter') { return 'variable'; }
                if (kind === 'class') { return 'class'; }
                if (kind === 'interface' || kind === 'type') { return 'interface'; }
                if (kind === 'enum') { return 'enum'; }
                if (kind === 'keyword') { return 'keyword'; }
                return 'text';
            }
            function wireLanguageService(ts, vfsMod, autocompleteMod, lintMod) {
                var compilerOptions = {
                    target: ts.ScriptTarget.ES2020,
                    module: ts.ModuleKind.ESNext,
                    moduleResolution: ts.ModuleResolutionKind.Bundler || ts.ModuleResolutionKind.NodeNext,
                    lib: ['es2020', 'dom', 'dom.iterable'],
                    allowJs: true, checkJs: false, strict: false, noImplicitAny: false,
                    jsx: ts.JsxEmit.Preserve, allowNonTsExtensions: true, skipLibCheck: true, noEmit: true
                };
                // Fetch the standard lib files from jsdelivr (raw npm) — reliable + CORS-enabled — instead of the
                // deprecated azure CDN default. No localStorage cache (avoids quota with the ~500kb dom lib).
                function libFetch(url) {
                    var name = url.slice(url.lastIndexOf('/') + 1);
                    return fetch('https://cdn.jsdelivr.net/npm/typescript@' + ts.version + '/lib/' + name);
                }
                vfsMod.createDefaultMapFromCDN(compilerOptions, ts.version, false, ts, undefined, libFetch)
                    .then(function (fsMap) {
                        fsMap.set('/nnz.d.ts', sdkTypes || '// nnz SDK types unavailable');
                        fsMap.set('/main.tsx', ' ');
                        var system = vfsMod.createSystem(fsMap);
                        var env = vfsMod.createVirtualTypeScriptEnvironment(system, ['/nnz.d.ts', '/main.tsx'], ts, compilerOptions);
                        slot.tsEnv = env;

                        function syncDoc(doc) {
                            env.updateFile('/main.tsx', doc.length ? doc : ' ');
                        }
                        function completionSource(context) {
                            if (!isJsFamily(slot.active)) { return null; }
                            syncDoc(context.state.doc.toString());
                            var word = context.matchBefore(/[A-Za-z0-9_]*/);
                            var pos = context.pos;
                            if (!context.explicit && word && word.from === word.to) {
                                // Only auto-open after a member '.' or while typing an identifier.
                                var prev = pos > 0 ? context.state.doc.sliceString(pos - 1, pos) : '';
                                if (prev !== '.') { return null; }
                            }
                            var info;
                            try { info = slot.tsEnv.languageService.getCompletionsAtPosition('/main.tsx', pos, {}); }
                            catch (e) { return null; }
                            if (!info || !info.entries) { return null; }
                            var from = word ? word.from : pos;
                            var options = [];
                            var limit = Math.min(info.entries.length, 200);
                            for (var i = 0; i < limit; i++) {
                                var en = info.entries[i];
                                options.push({ label: en.name, type: tsKindToCmType(en.kind) });
                            }
                            return { from: from, options: options };
                        }
                        function tsLinter(view) {
                            syncDoc(view.state.doc.toString());
                            var diags = [];
                            var svc = slot.tsEnv.languageService;
                            var all;
                            try {
                                all = svc.getSyntacticDiagnostics('/main.tsx').concat(svc.getSemanticDiagnostics('/main.tsx'));
                            } catch (e) { return diags; }
                            var docLen = view.state.doc.length;
                            for (var i = 0; i < all.length; i++) {
                                var d = all[i];
                                if (d.start == null || d.length == null) { continue; }
                                var from = Math.max(0, Math.min(d.start, docLen));
                                var to = Math.max(from, Math.min(d.start + d.length, docLen));
                                diags.push({
                                    from: from, to: to,
                                    severity: d.category === 1 ? 'error' : (d.category === 0 ? 'warning' : 'info'),
                                    message: ts.flattenDiagnosticMessageText(d.messageText, '\n')
                                });
                            }
                            return diags;
                        }
                        slot.tsExtensions = [
                            autocompleteMod.autocompletion({ override: [completionSource] }),
                            lintMod.lintGutter(),
                            lintMod.linter(tsLinter, { delay: 400 })
                        ];
                        slot.tsReady = true;
                        // Fold the service into the live view for the active file (and every later swap via extsFor).
                        if (slot.view && slot.tsCompartment) {
                            slot.view.dispatch({ effects: slot.tsCompartment.reconfigure(tsExtFor(slot.active)) });
                        }
                    })
                    .catch(function () { /* no autocomplete — silently degrade */ });
            }
            function loadLanguageService() {
                var dynImport2 = new Function('u', 'return import(u);');
                Promise.all([
                    dynImport2('https://esm.sh/typescript@5.6.3'),
                    dynImport2('https://esm.sh/@typescript/vfs@1.6.0'),
                    // Same-state pin as the base editor — the autocomplete + lint extensions land in the live view's
                    // Compartment, so they must share the one @codemirror/state instance or they never take effect.
                    dynImport2('https://esm.sh/@codemirror/autocomplete@6.18.1?deps=@codemirror/state@6.4.1'),
                    dynImport2('https://esm.sh/@codemirror/lint@6.8.1?deps=@codemirror/state@6.4.1')
                ]).then(function (mods) {
                    if (slot.status === 'closed') { return; }
                    var tsMod = mods[0], vfsMod = mods[1], acMod = mods[2], lintMod = mods[3];
                    var ts = tsMod.default || tsMod;
                    if (!ts || !ts.createLanguageService) { return; }
                    wireLanguageService(ts, vfsMod, acMod, lintMod);
                }).catch(function () { /* degrade silently */ });
            }

            // ── esbuild-wasm (client-side dev preview; pinned to the server's esbuild 0.28.1) ────────────────────
            // Initialised at most once per page (guarded on globalThis), reused across editor opens.
            function loadEsbuild() {
                if (slot.previewMode !== 'esbuild') {
                    showPreviewNote(slot.previewNoteText, false);
                    if (slot.previewMode === 'html') { renderHtmlDirect(); }
                    return;
                }
                showPreviewNote('Starting preview…', false);
                if (!globalThis.__nnzEsbuild) {
                    var dynImport3 = new Function('u', 'return import(u);');
                    globalThis.__nnzEsbuild = dynImport3('https://esm.sh/esbuild-wasm@0.28.1').then(function (m) {
                        // esm.sh nests the CJS module under .default — the top-level namespace has no initialize/build.
                        var eb = m.default || m;
                        return eb.initialize({ wasmURL: 'https://esm.sh/esbuild-wasm@0.28.1/esbuild.wasm' }).then(function () { return eb; });
                    });
                }
                globalThis.__nnzEsbuild.then(function (esbuild) {
                    if (slot.status === 'closed') { return; }
                    slot.esbuild = esbuild;
                    buildEsbuildPreview();
                }).catch(function (err) {
                    globalThis.__nnzEsbuild = null;
                    var msg = (err && err.message) ? err.message : String(err);
                    showPreviewNote('Live preview unavailable (esbuild-wasm could not load):\n' + msg + '\n\nSave & Compile still builds on the server.', true);
                });
            }

            // Load CodeMirror 6 + its languages/theme from a CDN. On any failure fall back to the textarea.
            // CRITICAL: every CodeMirror package that contributes facets (basicSetup, the languages, the theme)
            // MUST resolve the SAME @codemirror/state instance that EditorState.create uses, or its facets — the
            // language parser, the highlight style, AND the height/scroll theme — register against a different
            // state module and silently never apply (flat monochrome text, no scrollbar). esm.sh serves each
            // package its own bundled copy of @codemirror/state unless pinned, so pin them all to ours via ?deps.
            var dynImport = new Function('u', 'return import(u);');
            var cmPin = '?deps=@codemirror/state@6.4.1';
            Promise.all([
                dynImport('https://esm.sh/codemirror@6.0.1' + cmPin),
                dynImport('https://esm.sh/@codemirror/state@6.4.1'),
                dynImport('https://esm.sh/@codemirror/lang-html@6.4.9' + cmPin),
                dynImport('https://esm.sh/@codemirror/lang-javascript@6.2.2' + cmPin),
                dynImport('https://esm.sh/@codemirror/theme-one-dark@6.1.2' + cmPin)
            ]).then(function (mods) {
                if (slot.textarea || slot.status === 'closed') { return; }
                var cm = mods[0], cmState = mods[1], langHtml = mods[2], langJs = mods[3], dark = mods[4];
                slot.CmState = cmState.EditorState;
                slot.langHtml = langHtml.html;
                slot.langJs = langJs.javascript;
                slot.tsCompartment = new cmState.Compartment();
                slot.baseExt = [
                    cm.basicSetup,
                    dark.oneDark,
                    cm.EditorView.updateListener.of(function (u) { if (u.docChanged) { schedulePreview(); } }),
                    cm.EditorView.theme({
                        '&': { height: '100%' },
                        '.cm-scroller': { overflow: 'auto', fontFamily: '\"Cascadia Code\",\"Fira Code\",Menlo,Consolas,monospace' }
                    })
                ];
                var state = slot.CmState.create({ doc: slot.files[slot.active] || '', extensions: extsFor(slot.active) });
                var view = new cm.EditorView({ state: state, parent: host });
                slot.view = view;
                slot.CmView = cm.EditorView;
                view.focus();
                loadLanguageService();
            }).catch(function () { mountTextarea(); });

            setTimeout(function () {
                if (!slot.view && !slot.textarea && slot.status !== 'closed') { mountTextarea(); }
            }, 2500);

            // Kick off the preview independently of the editor surface.
            loadEsbuild();

            saveBtn.focus();
        }"""
    )
}
