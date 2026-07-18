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

// Web multi-file project editor — a full-screen DOM overlay hosting a file list + tabs + ONE CodeMirror 6 view,
// mounted above the Compose canvas. It extends CustomCodeEditor.wasmJs to a whole `src/` project:
//   • the file map lives on the JS slot (`globalThis.__nnzProjectEdit.files`, a `path → content` object);
//   • the left sidebar lists the files (add / rename / delete; the entry file is pinned);
//   • ONE CodeMirror view is reused — switching the active file flushes the current doc back into the map and
//     re-creates the view's EditorState with the new file's content + a language matched to the extension;
//   • "Save & Compile" stages the whole map as JSON and Kotlin round-trips it to the caller's compile.
//
// Handshake (same global-slot polling as CustomCodeEditor): JS stages { status, pendingFilesJson, ... } and Kotlin
// polls it. `compile` → JS stages the full file JSON; Kotlin flips 'busy', awaits the caller's compile, paints the
// result, flips back to 'editing'. 'closed' → Kotlin returns. CodeMirror loads from a CDN via a Function()-hidden
// dynamic import (webpack rejects a literal import()); a <textarea> fallback keeps the editor usable offline.
//
// CRITICAL: the overlay mounts into `document.body.shadowRoot || document.body`. Compose/Wasm renders the app into
// a shadow root, and a light-DOM child of a shadow host is NOT laid out — appending to document.body would leave
// the overlay 0×0 and invisible.
private val filesJson: Json = Json { encodeDefaults = true }
private val filesSerializer = MapSerializer(String.serializer(), String.serializer())

actual class ProjectEditor : ProjectEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialFiles: Map<String, String>,
        entryPath: String,
        language: String,
        compile: suspend (Map<String, String>) -> CompileFeedback,
    ) {
        openProjectEditor(title, filesJson.encodeToString(filesSerializer, initialFiles), entryPath, language)
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
        "{ var s = globalThis.__nnzProjectEdit; if (s && s.el && s.el.parentNode) { s.el.parentNode.removeChild(s.el); } globalThis.__nnzProjectEdit = null; }"
    )
}

// Builds the full-screen multi-file editor overlay and stages its state on globalThis.__nnzProjectEdit.
// `title`, `initialFilesJson`, `entryPath`, and `language` are the enclosing function's parameters — referenced
// directly in the JS body (Kotlin/Wasm js() marshals them as real JS values, so there is no string-injection
// surface).
private fun openProjectEditor(
    title: String,
    initialFilesJson: String,
    entryPath: String,
    language: String,
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
                CmView: null, CmState: null, langHtml: null, langJs: null, baseExt: null
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

            // Body: a fixed-width file sidebar + the main editor column (tabs strip over the CodeMirror host).
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

            body.appendChild(sidebar);
            body.appendChild(mainCol);

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
            function langExtFor(path) {
                var ext = (path.split('.').pop() || '').toLowerCase();
                if (ext === 'js' || ext === 'jsx' || ext === 'ts' || ext === 'tsx') {
                    return slot.langJs ? [slot.langJs()] : [];
                }
                return slot.langHtml ? [slot.langHtml()] : [];
            }
            function applyDoc(path) {
                if (slot.view && slot.CmState && slot.baseExt) {
                    var state = slot.CmState.create({ doc: slot.files[path] || '', extensions: slot.baseExt.concat(langExtFor(path)) });
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
            }
            function deleteFile(path) {
                if (path === slot.entry) { return; }
                delete slot.files[path];
                if (slot.active === path) { slot.active = slot.entry; }
                applyDoc(slot.active);
                renderFiles();
                renderTabs();
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
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); doClose(); }
                else if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) { e.preventDefault(); doSave(); }
            });

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
                host.appendChild(ta);
                slot.textarea = ta;
                ta.focus();
            }

            renderFiles();
            renderTabs();

            // Load CodeMirror 6 + its languages/theme from a CDN. On any failure fall back to the textarea.
            var dynImport = new Function('u', 'return import(u);');
            Promise.all([
                dynImport('https://esm.sh/codemirror@6.0.1'),
                dynImport('https://esm.sh/@codemirror/state@6.4.1'),
                dynImport('https://esm.sh/@codemirror/lang-html@6.4.9'),
                dynImport('https://esm.sh/@codemirror/lang-javascript@6.2.2'),
                dynImport('https://esm.sh/@codemirror/theme-one-dark@6.1.2')
            ]).then(function (mods) {
                if (slot.textarea || slot.status === 'closed') { return; }
                var cm = mods[0], cmState = mods[1], langHtml = mods[2], langJs = mods[3], dark = mods[4];
                slot.CmState = cmState.EditorState;
                slot.langHtml = langHtml.html;
                slot.langJs = langJs.javascript;
                slot.baseExt = [
                    cm.basicSetup,
                    dark.oneDark,
                    cm.EditorView.theme({
                        '&': { height: '100%' },
                        '.cm-scroller': { overflow: 'auto', fontFamily: '\"Cascadia Code\",\"Fira Code\",Menlo,Consolas,monospace' }
                    })
                ];
                var state = slot.CmState.create({ doc: slot.files[slot.active] || '', extensions: slot.baseExt.concat(langExtFor(slot.active)) });
                var view = new cm.EditorView({ state: state, parent: host });
                slot.view = view;
                slot.CmView = cm.EditorView;
                view.focus();
            }).catch(function () { mountTextarea(); });

            setTimeout(function () {
                if (!slot.view && !slot.textarea && slot.status !== 'closed') { mountTextarea(); }
            }, 2500);

            saveBtn.focus();
        }"""
    )
}
