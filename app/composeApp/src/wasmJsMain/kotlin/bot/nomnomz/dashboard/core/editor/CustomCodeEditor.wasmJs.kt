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

// Web custom-code editor — a full-screen DOM overlay hosting CodeMirror 6 (VS Code-like), mounted above the
// Compose canvas. It is a compile-on-save surface: the overlay stays open and shows the build result inline.
//
// Handshake (same global-slot polling as AudioFilePicker.wasmJs.kt): the JS side stages its state on
// `globalThis.__nnzCodeEdit` ({ status, pendingSource, ... }) and Kotlin polls it. When the operator clicks
// "Save & Compile" the JS sets status='compile' and stages the current source; Kotlin picks it up, flips
// status='busy' to avoid re-reading the same request, awaits the caller's compile, then paints the result and
// flips back to status='editing'. "Close" (button/Esc) sets status='closed' and Kotlin returns. CodeMirror is
// loaded from a CDN; if that fails (offline self-host, blocked CDN) the overlay falls back to a monospace
// <textarea> so the editor is never unavailable.
actual class CustomCodeEditor : CustomCodeEditorIO {
    actual override suspend fun editAndCompile(
        title: String,
        initialCode: String,
        language: String,
        compile: suspend (String) -> CompileFeedback,
    ) {
        openCodeEditor(title, initialCode, language)
        try {
            while (true) {
                when (codeEditorStatus()) {
                    "editing", "busy" -> delay(80)
                    "compile" -> {
                        val source: String = codeEditorSource()
                        beginCompile()
                        val feedback: CompileFeedback = compile(source)
                        reportCompile(feedback.ok, feedback.message)
                    }
                    else -> return
                }
            }
        } finally {
            closeCodeEditor()
        }
    }
}

private fun codeEditorStatus(): String =
    js("(globalThis.__nnzCodeEdit ? globalThis.__nnzCodeEdit.status : 'closed')")

// The source staged by the JS side at the moment "Save & Compile" was pressed (the value the operator asked to build).
private fun codeEditorSource(): String =
    js(
        "(globalThis.__nnzCodeEdit && typeof globalThis.__nnzCodeEdit.pendingSource === 'string') ? globalThis.__nnzCodeEdit.pendingSource : ''"
    )

// Guard: flip the slot out of 'compile' so the poll loop does not read the same save twice while the build runs.
private fun beginCompile() {
    js("{ var s = globalThis.__nnzCodeEdit; if (s) { s.status = 'busy'; } }")
}

// Paint the build result inline, re-enable the save button, and return the editor to the editable state.
private fun reportCompile(ok: Boolean, message: String) {
    js(
        """{
            var s = globalThis.__nnzCodeEdit;
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

// Tears the overlay out of the DOM and clears the slot. Removing the element also disposes its CodeMirror view
// (the view lives inside the removed subtree), so there is nothing else to release.
private fun closeCodeEditor() {
    js(
        "{ var s = globalThis.__nnzCodeEdit; if (s && s.el && s.el.parentNode) { s.el.parentNode.removeChild(s.el); } globalThis.__nnzCodeEdit = null; }"
    )
}

// Builds the full-screen editor overlay and stages its state on globalThis.__nnzCodeEdit. `title`, `initialCode`,
// and `language` are the enclosing function's parameters — referenced directly in the JS body (the Kotlin/Wasm
// js() interop marshals them as real JS values, so there is no string-injection surface).
private fun openCodeEditor(title: String, initialCode: String, language: String) {
    js(
        """{
            var slot = { status: 'editing', value: initialCode, pendingSource: '', el: null, view: null, textarea: null, result: null, saveBtn: null };
            globalThis.__nnzCodeEdit = slot;

            var overlay = document.createElement('div');
            overlay.setAttribute('data-nnz-code-editor', '');
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

            // The inline build-result strip — hidden until the first compile reports back.
            var result = document.createElement('div');
            result.style.cssText = 'display:none;padding:8px 16px;font-size:13px;font-weight:600;border-bottom:1px solid #262626;';
            slot.result = result;

            var host = document.createElement('div');
            host.style.cssText = 'flex:1;min-height:0;overflow:hidden;position:relative;';

            overlay.appendChild(header);
            overlay.appendChild(result);
            overlay.appendChild(host);
            // Compose/Wasm renders the whole app into a shadow root attached to <body>. Light-DOM children of a
            // shadow host are NOT laid out (there is no <slot> to project them), so appending the overlay to
            // document.body leaves it — and everything inside it — 0x0 and invisible. Mount it INTO that shadow
            // root instead, where it lays out against the viewport and paints above the canvas. Fall back to
            // document.body if the app is ever hosted without a shadow root (e.g. a plain-DOM Compose target).
            var mountRoot = document.body.shadowRoot || document.body;
            mountRoot.appendChild(overlay);

            function currentValue() {
                if (slot.view) { return slot.view.state.doc.toString(); }
                if (slot.textarea) { return slot.textarea.value; }
                return slot.value;
            }
            function doSave() {
                if (slot.status !== 'editing') { return; }
                slot.pendingSource = currentValue();
                slot.status = 'compile';
                saveBtn.disabled = true;
                saveBtn.textContent = 'Compiling…';
                result.style.display = 'none';
            }
            function doClose() { slot.status = 'closed'; }

            saveBtn.addEventListener('click', doSave);
            closeBtn.addEventListener('click', doClose);
            overlay.addEventListener('keydown', function (e) {
                if (e.key === 'Escape') { e.preventDefault(); doClose(); }
                else if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) { e.preventDefault(); doSave(); }
            });

            function mountTextarea() {
                if (slot.textarea || slot.view) { return; }
                var ta = document.createElement('textarea');
                ta.value = slot.value;
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

            // Load CodeMirror 6 (the VS Code-like experience) from a CDN. On any failure, or if the overlay was
            // already closed, fall back to / keep the monospace textarea so the editor always works.
            //
            // The import is built through the Function constructor rather than written as a literal import(): the
            // Kotlin/Wasm webpack target statically rejects a literal dynamic import() ("target doesn't support
            // dynamic import syntax") and would fail the bundle. Hiding it behind Function() leaves webpack out of
            // it and the browser runs a real native import() at runtime.
            var dynImport = new Function('u', 'return import(u);');
            Promise.all([
                dynImport('https://esm.sh/codemirror@6.0.1'),
                dynImport('https://esm.sh/@codemirror/lang-html@6.4.9'),
                dynImport('https://esm.sh/@codemirror/theme-one-dark@6.1.2')
            ]).then(function (mods) {
                if (slot.textarea || slot.status === 'closed') { return; }
                var cm = mods[0], langHtml = mods[1], dark = mods[2];
                var view = new cm.EditorView({
                    doc: slot.value,
                    extensions: [
                        cm.basicSetup,
                        langHtml.html(),
                        dark.oneDark,
                        cm.EditorView.theme({
                            '&': { height: '100%' },
                            '.cm-scroller': { overflow: 'auto', fontFamily: '\"Cascadia Code\",\"Fira Code\",Menlo,Consolas,monospace' }
                        })
                    ],
                    parent: host
                });
                slot.view = view;
                view.focus();
            }).catch(function () { mountTextarea(); });

            // If the CDN modules are slow, show the textarea after a short grace period rather than a blank host.
            setTimeout(function () {
                if (!slot.view && !slot.textarea && slot.status !== 'closed') { mountTextarea(); }
            }, 2500);

            saveBtn.focus();
        }"""
    )
}
