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

// Web custom-code editor — a full-screen DOM overlay hosting the VS Code editor (Monaco), mounted above the
// Compose canvas. It is a compile-on-save surface: the overlay stays open and shows the build result inline.
//
// IntelliSense stack (loaded from a CDN at runtime; this project has no JS bundler, so everything is
// hand-wired):
//   • Monaco Editor, via the classic AMD CDN loader. Monaco's own language workers (TypeScript, HTML, CSS,
//     JSON) are spun up through the standard `MonacoEnvironment.getWorker` proxy, giving real completion,
//     hover and diagnostics for `lang="ts"`/`lang="js"` scripts and for standalone ts/js/html/css/json
//     widgets out of the box.
//   • For a Vue SFC (`language == "vue"`) we additionally attempt the Vue language service (Volar) in a Web
//     Worker: a Blob module-worker runs `@volar/monaco/worker` + `@vue/language-service` with a jsdelivr-backed
//     virtual FS (`@volar/jsdelivr`) for on-demand type acquisition, wired to Monaco through the documented
//     `@volar/monaco` main-thread helpers (activateMarkers / activateAutoInsertion / registerProviders). This
//     gives `<template>` component/prop/directive completion and Vue-aware `<script setup lang="ts">` checking.
//     It is a best-effort enhancement layered on top of an already-working editor: any failure to load or
//     initialise leaves the plain Monaco editor (with its native TS/HTML/CSS IntelliSense) untouched.
//
// Degradation ladder: Volar → plain Monaco → monospace <textarea>. If Monaco itself cannot load (offline
// self-host, blocked CDN) the overlay falls back to a <textarea> so the editor is never unavailable.
//
// Handshake (same global-slot polling as AudioFilePicker.wasmJs.kt): the JS side stages its state on
// `globalThis.__nnzCodeEdit` ({ status, pendingSource, ... }) and Kotlin polls it. When the operator clicks
// "Save & Compile" the JS sets status='compile' and stages the current source; Kotlin picks it up, flips
// status='busy' to avoid re-reading the same request, awaits the caller's compile, then paints the result and
// flips back to status='editing'. "Close" (button/Esc) sets status='closed' and Kotlin returns.
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

// Tears the overlay out of the DOM and disposes the Monaco resources it owned. Removing the element disposes the
// editor view (it lives inside the removed subtree); we also dispose the model, terminate the Volar worker, and
// revoke the worker blob URL so a repeated open/close cycle does not leak.
private fun closeCodeEditor() {
    js(
        """{
            var s = globalThis.__nnzCodeEdit;
            if (s) {
                try { if (s.editor && s.editor.dispose) { s.editor.dispose(); } } catch (e) {}
                try { if (s.model && s.model.dispose) { s.model.dispose(); } } catch (e) {}
                try { if (s.worker && s.worker.dispose) { s.worker.dispose(); } } catch (e) {}
                try { if (s.vueWorkerUrl) { URL.revokeObjectURL(s.vueWorkerUrl); } } catch (e) {}
                try { if (s.el && s.el.parentNode) { s.el.parentNode.removeChild(s.el); } } catch (e) {}
            }
            globalThis.__nnzCodeEdit = null;
        }"""
    )
}

// Builds the full-screen editor overlay and stages its state on globalThis.__nnzCodeEdit. `title`, `initialCode`,
// and `language` are the enclosing function's parameters — referenced directly in the JS body (the Kotlin/Wasm
// js() interop marshals them as real JS values, so there is no string-injection surface).
private fun openCodeEditor(title: String, initialCode: String, language: String) {
    js(
        """{
            var slot = {
                status: 'editing', value: initialCode, pendingSource: '',
                el: null, monaco: null, editor: null, model: null, textarea: null,
                result: null, saveBtn: null, worker: null, vueWorkerUrl: null
            };
            globalThis.__nnzCodeEdit = slot;

            // --- Overlay chrome -------------------------------------------------------------------------------
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
            document.body.appendChild(overlay);

            // --- Compile-on-save handshake --------------------------------------------------------------------
            function currentValue() {
                if (slot.model) { return slot.model.getValue(); }
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

            // --- Textarea fallback (offline self-host / blocked CDN) ------------------------------------------
            function mountTextarea() {
                if (slot.textarea || slot.editor) { return; }
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

            // --- Language mapping -----------------------------------------------------------------------------
            var langArg = (language || '').toLowerCase();
            function mapMonacoLang(l) {
                if (l === 'vue') { return 'vue'; }
                if (l === 'ts' || l === 'typescript' || l === 'tsx' || l === 'react' || l === 'jsx' || l === 'solid' || l === 'preact') { return 'typescript'; }
                if (l === 'js' || l === 'javascript') { return 'javascript'; }
                if (l === 'css' || l === 'scss' || l === 'less') { return 'css'; }
                if (l === 'json') { return 'json'; }
                // html, vanilla, svelte, empty, and anything else edit best as HTML (tags + embedded script/style).
                return 'html';
            }
            function extFor(ml) {
                if (ml === 'typescript') { return 'ts'; }
                if (ml === 'javascript') { return 'js'; }
                if (ml === 'css') { return 'css'; }
                if (ml === 'json') { return 'json'; }
                if (ml === 'vue') { return 'vue'; }
                return 'html';
            }
            var monacoLang = mapMonacoLang(langArg);

            var MONACO_VS = 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';

            // Minimal ambient types so a standalone `import { ref } from 'vue'` resolves under Monaco's own TS
            // worker. The Volar path (when it loads) supersedes this with the real Vue types fetched from jsdelivr.
            var VUE_TYPE_SHIM = [
                'declare module \"vue\" {',
                '  export function ref<T>(value: T): { value: T };',
                '  export function reactive<T extends object>(target: T): T;',
                '  export function computed<T>(getter: () => T): { readonly value: T };',
                '  export function watch(source: any, cb: (a: any, b: any) => void, options?: any): void;',
                '  export function watchEffect(cb: () => void): void;',
                '  export function onMounted(cb: () => void): void;',
                '  export function onUnmounted(cb: () => void): void;',
                '  export function onBeforeMount(cb: () => void): void;',
                '  export function nextTick(cb?: () => void): Promise<void>;',
                '  export function defineComponent(options: any): any;',
                '  export function defineProps<T = any>(): T;',
                '  export function defineEmits<T = any>(): T;',
                '  export const provide: any;',
                '  export const inject: any;',
                '  export const h: any;',
                '}'
            ].join('\n');

            // A compact HTML-derived Monarch grammar for the `vue` language: tags/attributes/comments/{{ }}, with
            // <script> embedded as TypeScript and <style> as CSS. Wrapped in try/catch at the call site so a
            // grammar hiccup only costs colouring, never the editor.
            var VUE_MONARCH = {
                defaultToken: '',
                tokenPostfix: '.vue',
                ignoreCase: true,
                tokenizer: {
                    root: [
                        [/<!--/, 'comment', '@comment'],
                        [/<script/, { token: 'tag', next: '@scriptEmbedded', nextEmbedded: 'typescript' }],
                        [/<style/, { token: 'tag', next: '@styleEmbedded', nextEmbedded: 'css' }],
                        [/<\/?[a-zA-Z][\w:-]*/, 'tag'],
                        [/{{/, { token: 'delimiter.bracket', next: '@interp' }],
                        [/[a-zA-Z@:][\w@:.-]*=/, 'attribute.name'],
                        [/"[^"]*"/, 'attribute.value'],
                        [/'[^']*'/, 'attribute.value'],
                        [/[<>]/, 'tag'],
                        [/[^<{]+/, '']
                    ],
                    comment: [
                        [/-->/, 'comment', '@pop'],
                        [/[^-]+/, 'comment'],
                        [/./, 'comment']
                    ],
                    interp: [
                        [/}}/, { token: 'delimiter.bracket', next: '@pop' }],
                        [/[^}]+/, 'variable']
                    ],
                    scriptEmbedded: [
                        [/<\/script\s*>/, { token: '@rematch', next: '@pop', nextEmbedded: '@pop' }]
                    ],
                    styleEmbedded: [
                        [/<\/style\s*>/, { token: '@rematch', next: '@pop', nextEmbedded: '@pop' }]
                    ]
                }
            };

            var VUE_LANG_CONFIG = {
                comments: { blockComment: ['<!--', '-->'] },
                brackets: [['<', '>'], ['{', '}'], ['(', ')'], ['[', ']']],
                autoClosingPairs: [
                    { open: '{', close: '}' },
                    { open: '[', close: ']' },
                    { open: '(', close: ')' },
                    { open: '\'', close: '\'' },
                    { open: '\"', close: '\"' },
                    { open: '<', close: '>' }
                ]
            };

            // --- Volar (Vue language service) — best-effort enhancement over a working Monaco -----------------
            // Builds the Web Worker source that runs @vue/language-service inside @volar/monaco's TS worker host,
            // with a jsdelivr-backed virtual FS for on-demand type acquisition. Pinned CDN ESM, no bundler.
            function buildVueWorkerBlobUrl() {
                var src = [
                    "import * as worker from 'https://esm.sh/monaco-editor@0.52.2/esm/vs/editor/editor.worker.js';",
                    "import { createTypeScriptWorkerLanguageService } from 'https://esm.sh/@volar/monaco@2.4.11/worker';",
                    "import { createNpmFileSystem } from 'https://esm.sh/@volar/jsdelivr@2.4.11';",
                    "import { createVueLanguagePlugin, getFullLanguageServicePlugins } from 'https://esm.sh/@vue/language-service@2.1.10';",
                    "import ts from 'https://esm.sh/typescript@5.6.3';",
                    "import { URI } from 'https://esm.sh/vscode-uri@3.0.8';",
                    "self.onmessage = function () {",
                    "  worker.initialize(function (ctx) {",
                    "    var compilerOptions = {",
                    "      target: 99, module: 99, moduleResolution: 2, allowJs: true, checkJs: false,",
                    "      jsx: 1, allowNonTsExtensions: true, esModuleInterop: true, skipLibCheck: true,",
                    "      lib: ['esnext', 'dom', 'dom.iterable']",
                    "    };",
                    "    var env = { workspaceFolders: [URI.file('/')], fs: createNpmFileSystem() };",
                    "    var vueLanguagePlugin = createVueLanguagePlugin(ts, compilerOptions, {});",
                    "    var servicePlugins = getFullLanguageServicePlugins(ts);",
                    "    return createTypeScriptWorkerLanguageService({",
                    "      typescript: ts,",
                    "      compilerOptions: compilerOptions,",
                    "      workerContext: ctx,",
                    "      env: env,",
                    "      uriConverter: {",
                    "        asFileName: function (u) { return u.fsPath; },",
                    "        asUri: function (f) { return URI.file(f); }",
                    "      },",
                    "      languagePlugins: [vueLanguagePlugin],",
                    "      languageServicePlugins: servicePlugins",
                    "    });",
                    "  });",
                    "};"
                ].join('\n');
                return URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
            }

            function activateVolar(monaco) {
                var dynImport = new Function('u', 'return import(u);');
                return dynImport('https://esm.sh/@volar/monaco@2.4.11').then(function (volar) {
                    if (slot.status === 'closed') { return; }
                    slot.vueWorkerUrl = buildVueWorkerBlobUrl();
                    var worker = monaco.editor.createWebWorker({ moduleId: 'nnz-vue-worker', label: 'vue', createData: {} });
                    slot.worker = worker;
                    var getSyncUris = function () { return monaco.editor.getModels().map(function (m) { return m.uri; }); };
                    volar.activateMarkers(worker, ['vue'], 'nnz-vue-markers', getSyncUris, monaco.editor);
                    volar.activateAutoInsertion(worker, ['vue'], getSyncUris, monaco.editor);
                    return volar.registerProviders(worker, ['vue'], getSyncUris, monaco.languages);
                });
            }

            // --- Monaco bring-up ------------------------------------------------------------------------------
            function setupMonaco(monaco) {
                slot.monaco = monaco;
                try {
                    monaco.editor.defineTheme('nnz-dark', {
                        base: 'vs-dark', inherit: true, rules: [], colors: { 'editor.background': '#0a0a0a' }
                    });
                } catch (e) {}

                try {
                    var tsl = monaco.languages.typescript;
                    if (tsl) {
                        tsl.typescriptDefaults.setCompilerOptions({
                            target: tsl.ScriptTarget.ESNext,
                            module: tsl.ModuleKind.ESNext,
                            moduleResolution: tsl.ModuleResolutionKind.NodeJs,
                            jsx: tsl.JsxEmit.Preserve,
                            allowNonTsExtensions: true,
                            allowJs: true,
                            esModuleInterop: true,
                            skipLibCheck: true,
                            noEmit: true
                        });
                        tsl.typescriptDefaults.setEagerModelSync(true);
                        tsl.typescriptDefaults.addExtraLib(VUE_TYPE_SHIM, 'file:///node_modules/@vue/nnz-vue-shim.d.ts');
                    }
                } catch (e) {}

                if (monacoLang === 'vue') {
                    try {
                        var known = monaco.languages.getLanguages().some(function (l) { return l.id === 'vue'; });
                        if (!known) {
                            monaco.languages.register({ id: 'vue', extensions: ['.vue'] });
                            try { monaco.languages.setMonarchTokensProvider('vue', VUE_MONARCH); } catch (e) {}
                            try { monaco.languages.setLanguageConfiguration('vue', VUE_LANG_CONFIG); } catch (e) {}
                        }
                    } catch (e) {}
                }

                var uri = monaco.Uri.parse('file:///widget/Widget.' + extFor(monacoLang));
                var model = monaco.editor.getModel(uri);
                if (model) { try { model.dispose(); } catch (e) {} }
                model = monaco.editor.createModel(slot.value, monacoLang, uri);
                slot.model = model;

                var editor = monaco.editor.create(host, {
                    model: model,
                    theme: 'nnz-dark',
                    automaticLayout: true,
                    fontFamily: '\"Cascadia Code\",\"Fira Code\",Menlo,Consolas,monospace',
                    fontSize: 13,
                    tabSize: 2,
                    minimap: { enabled: false },
                    scrollBeyondLastLine: false,
                    fixedOverflowWidgets: true,
                    smoothScrolling: true
                });
                slot.editor = editor;
                editor.focus();
                editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, function () { doSave(); });

                if (monacoLang === 'vue') {
                    try { activateVolar(monaco).catch(function () {}); } catch (e) {}
                }
            }

            // --- Monaco loader (classic AMD CDN loader — reliably wires the built-in workers, no bundler) ------
            function loadScript(src, onload, onerror) {
                var scriptEl = document.createElement('script');
                scriptEl.src = src;
                scriptEl.async = true;
                scriptEl.onload = onload;
                scriptEl.onerror = onerror;
                document.head.appendChild(scriptEl);
            }

            if (globalThis.monaco && globalThis.monaco.editor) {
                try { setupMonaco(globalThis.monaco); } catch (e) { mountTextarea(); }
            } else {
                if (!globalThis.__nnzMonacoEnv) {
                    globalThis.__nnzMonacoEnv = true;
                    // Built-in Monaco workers: a tiny same-origin blob that importScripts the CDN worker main.
                    var proxySrc = 'self.MonacoEnvironment={baseUrl:\"' + MONACO_VS + '/\"};importScripts(\"' + MONACO_VS + '/base/worker/workerMain.js\");';
                    var builtinWorkerUrl = URL.createObjectURL(new Blob([proxySrc], { type: 'text/javascript' }));
                    globalThis.MonacoEnvironment = {
                        getWorker: function (moduleId, label) {
                            try {
                                var active = globalThis.__nnzCodeEdit;
                                if (label === 'vue' && active && active.vueWorkerUrl) {
                                    return new Worker(active.vueWorkerUrl, { type: 'module' });
                                }
                            } catch (e) {}
                            return new Worker(builtinWorkerUrl);
                        }
                    };
                }
                loadScript(MONACO_VS + '/loader.js', function () {
                    try {
                        globalThis.require.config({ paths: { vs: MONACO_VS } });
                        globalThis.require(['vs/editor/editor.main'], function () {
                            if (slot.status === 'closed') { return; }
                            try { setupMonaco(globalThis.monaco); } catch (e) { mountTextarea(); }
                        }, function () { mountTextarea(); });
                    } catch (e) { mountTextarea(); }
                }, function () { mountTextarea(); });
            }

            // If the CDN is slow or silently blocked, show the textarea rather than a blank host.
            setTimeout(function () {
                if (!slot.editor && !slot.textarea && slot.status !== 'closed') { mountTextarea(); }
            }, 6000);

            saveBtn.focus();
        }"""
    )
}
