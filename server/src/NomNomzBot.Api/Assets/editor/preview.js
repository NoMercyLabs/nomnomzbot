// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The editor's live preview: bundle the project in the browser with esbuild-wasm over an in-memory file
// system and render the result in a sandboxed iframe that reloads on edit.
//
// This is a DEV-LOOP convenience only. "Save & Compile" round-trips to the server, and that server build
// stays the trust boundary — nothing here decides whether a project is publishable.

const ESBUILD_URL = 'https://esm.sh/esbuild-wasm@0.28.1';
const ESBUILD_WASM_URL = 'https://esm.sh/esbuild-wasm@0.28.1/esbuild.wasm';

// The ?deps pin is load-bearing: without it esm.sh resolves the compiler's own `vue` copy, and the two
// Vue instances then disagree about what a component is.
const VUE_SFC_URL = 'https://esm.sh/@vue/compiler-sfc@3.5.39?deps=vue@3.5.39';

// Bare specifiers stay external in the bundle and resolve here instead, so react/vue are fetched once by
// the iframe rather than inlined into every rebuild.
const IMPORT_MAP = Object.freeze({
    imports: {
        react: 'https://esm.sh/react@18',
        'react-dom': 'https://esm.sh/react-dom@18',
        'react-dom/client': 'https://esm.sh/react-dom@18/client',
        'react/jsx-runtime': 'https://esm.sh/react@18/jsx-runtime',
        'react/jsx-dev-runtime': 'https://esm.sh/react@18/jsx-dev-runtime',
        vue: 'https://esm.sh/vue@3.5.39',
    },
});

// A Vue entry SFC exports a component but mounts nothing, so the bundle starts from a generated root that
// mounts it the way the overlay runtime does.
const VUE_ENTRY = '__nnz_vue_main__.js';

const REBUILD_DEBOUNCE_MS = 500;

const SCRIPT_NOTE = 'Code scripts run in the bot sandbox — press Save & Compile to validate.';

const ESBUILD_LOADER_BY_EXTENSION = Object.freeze({
    ts: 'ts',
    tsx: 'tsx',
    jsx: 'jsx',
    json: 'json',
    css: 'css',
});

const RESOLVE_SUFFIXES = Object.freeze([
    '',
    '.js',
    '.ts',
    '.jsx',
    '.tsx',
    '.mjs',
    '.vue',
    '.json',
    '/index.js',
    '/index.ts',
    '/index.jsx',
    '/index.tsx',
    '/index.vue',
]);

// Events a widget subscribes to, discovered from its own source: nnz.on('follow') / NomNomz.on("cheer").
const SUBSCRIPTION_PATTERN = /\.on\(\s*['"]([a-zA-Z0-9_.:-]+)['"]/g;
const NON_WIDGET_EVENTS = new Set(['message', 'error']);

function extensionOf(path) {
    const dot = path.lastIndexOf('.');
    return dot === -1 ? '' : path.slice(dot + 1).toLowerCase();
}

// A socket-free stand-in for the overlay SDK (window.NomNomz) so a widget mounts and subscribes with no hub
// behind it. Same surface as /overlay/sdk.js; a postMessage bridge lets the fire bar drive events and push
// settings. Injected as a classic script so it exists before the deferred module bundle runs.
function previewSdkStub() {
    return `(function () {
  var handlers = {}, anyHandlers = [], settingsHandlers = [];
  var settings = (window.WIDGET_SETTINGS && typeof window.WIDGET_SETTINGS === 'object') ? window.WIDGET_SETTINGS : {};
  function on(type, fn) { if (typeof fn === 'function') (handlers[type] = handlers[type] || []).push(fn); return api; }
  function off(type, fn) { var l = handlers[type]; if (l) handlers[type] = l.filter(function (h) { return h !== fn; }); return api; }
  function onAny(fn) { if (typeof fn === 'function') anyHandlers.push(fn); return api; }
  function onSettings(fn) { if (typeof fn === 'function') { settingsHandlers.push(fn); try { fn(settings); } catch (e) {} } return api; }
  function emit(type, data) {
    (handlers[type] || []).forEach(function (fn) { try { fn(data, type); } catch (e) { console.error(e); } });
    anyHandlers.forEach(function (fn) { try { fn(type, data); } catch (e) {} });
  }
  var api = {
    on: on, off: off, onAny: onAny, onSettings: onSettings,
    reportError: function (m) { console.error('[preview widget]', m); },
    get settings() { return settings; }
  };
  window.NomNomz = api;
  window.addEventListener('message', function (ev) {
    var m = ev.data;
    if (!m) return;
    if (m.__nnzFire) { emit(m.__nnzFire.type, m.__nnzFire.data || {}); }
    else if (m.__nnzSettings) { settings = m.__nnzSettings; settingsHandlers.forEach(function (fn) { try { fn(settings); } catch (e) {} }); }
  });
})();`;
}

export function initPreview({
    frame,
    note,
    fireBar,
    refresh,
    language,
    entry,
    fireSamples = {},
    noteText = '',
    snapshotFiles,
}) {
    const framework = String(language ?? '').toLowerCase();

    // Vue is the only framework whose preview mounts through the SDK stub, so it is also the only one whose
    // fire bar can drive anything.
    const isVue = framework === 'vue';
    const entryExtension = extensionOf(entry);
    const mode = framework === 'script'
        ? 'note'
        : isVue
          ? 'esbuild'
          : entryExtension === 'html' || entryExtension === 'htm'
            ? 'html'
            : 'esbuild';

    const idleNote = noteText || (mode === 'note' ? SCRIPT_NOTE : '');

    let esbuild = null;
    let vueSfc = null;
    let timer = 0;

    function showNote(text, isError) {
        frame.hidden = true;
        fireBar.hidden = true;
        note.hidden = false;
        note.dataset.error = String(Boolean(isError));
        note.textContent = text;
    }

    function showFrame(srcdoc) {
        note.hidden = true;
        frame.hidden = false;
        frame.srcdoc = srcdoc;
    }

    // ── Fire bar ───────────────────────────────────────────────────────────

    function subscribedEvents(files) {
        const events = new Set();
        for (const source of Object.values(files)) {
            SUBSCRIPTION_PATTERN.lastIndex = 0;
            let match;
            while ((match = SUBSCRIPTION_PATTERN.exec(source)) !== null) {
                if (!NON_WIDGET_EVENTS.has(match[1])) events.add(match[1]);
            }
        }
        return [...events];
    }

    function fireEvent(type) {
        const sample = fireSamples[type] ?? fireSamples._default ?? {};
        // '*' rather than the origin: a sandboxed frame without allow-same-origin has an opaque origin, which
        // matches no origin string at all.
        frame.contentWindow?.postMessage({ __nnzFire: { type, data: sample } }, '*');
    }

    function refreshFireBar(files) {
        if (!isVue) {
            fireBar.hidden = true;
            return;
        }

        const events = subscribedEvents(files);
        if (events.length === 0) {
            fireBar.replaceChildren();
            fireBar.hidden = true;
            return;
        }

        const label = document.createElement('span');
        label.className = 'fire-label';
        label.textContent = 'Fire event:';

        fireBar.replaceChildren(
            label,
            ...events.map((type) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'fire-btn';
                button.textContent = type;
                button.addEventListener('click', () => fireEvent(type));
                return button;
            }),
        );
        fireBar.hidden = false;
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    function renderBundle(files, javascript, css) {
        const reset =
            'html,body{margin:0;padding:0;background:transparent;color:#e5e5e5;font-family:-apple-system,BlinkMacSystemFont,sans-serif;}';
        const sdk = isVue ? `<script>${previewSdkStub()}<\/script>` : '';
        showFrame(
            '<!doctype html><html><head><meta charset="utf-8">' +
                `<style>${reset}</style><style>${css ?? ''}</style>` +
                `<script type="importmap">${JSON.stringify(IMPORT_MAP)}<\/script></head><body>` +
                `<div id="app"></div><div id="root"></div>${sdk}` +
                `<script type="module">${javascript}<\/script></body></html>`,
        );
        refreshFireBar(files);
    }

    function renderHtmlDirect() {
        const files = snapshotFiles();
        fireBar.hidden = true;
        showFrame(files[entry] ?? '');
    }

    // ── esbuild virtual file system ────────────────────────────────────────

    function resolveVfs(files, importer, specifier) {
        const slash = importer.lastIndexOf('/');
        const segments = slash === -1 ? [] : importer.slice(0, slash).split('/');
        for (const segment of specifier.split('/')) {
            if (segment === '.' || segment === '') continue;
            if (segment === '..') segments.pop();
            else segments.push(segment);
        }
        const base = segments.join('/');
        return RESOLVE_SUFFIXES.map((suffix) => base + suffix).find((candidate) => candidate in files) ?? null;
    }

    // Compile one SFC the way the server does (@vue/compiler-sfc): <script setup> with the template inlined,
    // a stable scope id, and scoped <style> injected at runtime. The output keeps TS syntax — esbuild strips
    // it via the 'ts' loader on the caller's side.
    function compileVueFile(path, source) {
        const parsed = vueSfc.parse(source, { filename: path });
        if (parsed.errors?.length) throw new Error(parsed.errors[0].message ?? String(parsed.errors[0]));

        const descriptor = parsed.descriptor;
        if (!descriptor.scriptSetup && !descriptor.script) throw new Error('SFC has no <script> block');

        let hash = 0;
        for (let i = 0; i < path.length; i++) hash = ((hash << 5) - hash + path.charCodeAt(i)) | 0;
        const id = Math.abs(hash).toString(36);

        const scoped = descriptor.styles.some((style) => style.scoped);
        const compiled = vueSfc.compileScript(descriptor, {
            id,
            inlineTemplate: true,
            templateOptions: { scoped },
            babelParserPlugins: ['typescript'],
        });

        // rewriteDefault re-parses the compiled script, which is still TS — it needs the plugin too.
        let code = vueSfc.rewriteDefault(compiled.content, '__sfc_main', ['typescript']);
        if (scoped) code += `\n__sfc_main.__scopeId = "data-v-${id}";`;

        let css = '';
        for (const style of descriptor.styles) {
            css += vueSfc.compileStyle({ source: style.content, filename: path, id, scoped: style.scoped }).code;
        }
        if (css) {
            code += `\n;(function(){var __st=document.createElement("style");__st.textContent=${JSON.stringify(css)};document.head.appendChild(__st);})();`;
        }

        return `${code}\nexport default __sfc_main;`;
    }

    function vueEntrySource() {
        return (
            `import __App from "./${entry}";\n` +
            'import { createApp } from "vue";\n' +
            'try { window.__nnzApp = createApp(__App); window.__nnzApp.mount("#app"); }\n' +
            'catch (e) { var d = document.getElementById("app"); if (d) { d.textContent = "Mount error: " + ((e && e.message) || e); d.style.color = "#f87171"; } }'
        );
    }

    function vfsPlugin(files) {
        return {
            name: 'nnz-vfs',
            setup(build) {
                build.onResolve({ filter: /.*/ }, (args) => {
                    if (args.kind === 'entry-point') return { path: args.path, namespace: 'nnzvfs' };
                    if (args.path.startsWith('.')) {
                        const resolved = resolveVfs(files, args.importer, args.path);
                        return resolved
                            ? { path: resolved, namespace: 'nnzvfs' }
                            : { errors: [{ text: `Cannot resolve ${args.path} from ${args.importer}` }] };
                    }
                    return { path: args.path, external: true };
                });

                build.onLoad({ filter: /.*/, namespace: 'nnzvfs' }, (args) => {
                    if (args.path === VUE_ENTRY) return { contents: vueEntrySource(), loader: 'js' };

                    const contents = files[args.path];
                    if (contents == null) return { errors: [{ text: `Missing file ${args.path}` }] };

                    if (args.path.endsWith('.vue')) {
                        try {
                            return { contents: compileVueFile(args.path, contents), loader: 'ts' };
                        } catch (error) {
                            return {
                                errors: [{ text: `Vue compile (${args.path}): ${error?.message ?? error}` }],
                            };
                        }
                    }
                    return { contents, loader: ESBUILD_LOADER_BY_EXTENSION[extensionOf(args.path)] ?? 'js' };
                });
            },
        };
    }

    async function buildBundle() {
        if (!esbuild) return;

        const files = snapshotFiles();
        if (!(entry in files)) {
            showNote(`Entry file ${entry} is missing.`, true);
            return;
        }

        if (isVue && !vueSfc) {
            showNote('Loading Vue compiler…', false);
            try {
                vueSfc = await loadVueSfc();
            } catch (error) {
                // Drop the cached rejection so the next edit retries instead of inheriting the failure.
                globalThis.__nnzVueSfc = null;
                showNote(`Vue compiler could not load:\n${error?.message ?? error}`, true);
                return;
            }
        }

        try {
            const result = await esbuild.build({
                entryPoints: [isVue ? VUE_ENTRY : entry],
                bundle: true,
                write: false,
                format: 'esm',
                target: 'es2020',
                outdir: 'nnzout',
                jsx: 'automatic',
                jsxImportSource: 'react',
                loader: {
                    '.png': 'dataurl',
                    '.jpg': 'dataurl',
                    '.jpeg': 'dataurl',
                    '.gif': 'dataurl',
                    '.svg': 'text',
                    '.woff': 'dataurl',
                    '.woff2': 'dataurl',
                },
                plugins: [vfsPlugin(files)],
            });

            const javascript = result.outputFiles.find((file) => file.path.endsWith('.js'))?.text ?? '';
            const css = result.outputFiles.find((file) => file.path.endsWith('.css'))?.text ?? '';
            renderBundle(files, javascript, css);
        } catch (error) {
            showNote(`Preview build failed:\n${error?.message ?? error}`, true);
        }
    }

    // ── Toolchain loading (once per page, shared across editor opens) ───────

    function loadVueSfc() {
        globalThis.__nnzVueSfc ??= import(VUE_SFC_URL).then((module) =>
            module?.parse ? module : (module?.default ?? module),
        );
        return globalThis.__nnzVueSfc;
    }

    function loadEsbuild() {
        // esm.sh nests the CJS module under .default — the top-level namespace has no initialize/build.
        globalThis.__nnzEsbuild ??= import(ESBUILD_URL)
            .then((module) => module.default ?? module)
            .then((module) => module.initialize({ wasmURL: ESBUILD_WASM_URL }).then(() => module));
        return globalThis.__nnzEsbuild;
    }

    // ── Public surface ─────────────────────────────────────────────────────

    function rebuildNow() {
        if (mode === 'esbuild') buildBundle();
        else if (mode === 'html') renderHtmlDirect();
        else showNote(idleNote, false);
    }

    function schedule() {
        if (mode !== 'esbuild' && mode !== 'html') return;
        clearTimeout(timer);
        timer = setTimeout(rebuildNow, REBUILD_DEBOUNCE_MS);
    }

    refresh.addEventListener('click', rebuildNow);

    if (mode !== 'esbuild') {
        rebuildNow();
    } else {
        showNote('Starting preview…', false);
        loadEsbuild()
            .then((loaded) => {
                esbuild = loaded;
                buildBundle();
            })
            .catch((error) => {
                // Drop the cached rejection so a later open can retry rather than inherit the failure.
                globalThis.__nnzEsbuild = null;
                showNote(
                    `Live preview unavailable (esbuild-wasm could not load):\n${error?.message ?? error}\n\n` +
                        'Save & Compile still builds on the server.',
                    true,
                );
            });
    }

    return { mode, schedule, rebuildNow };
}
