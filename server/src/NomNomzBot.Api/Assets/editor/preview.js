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

import { createSourceMap } from './preview-sourcemap.js';

// esbuild reports files of the in-memory file system under this namespace prefix.
const VFS_PREFIX = /^.*nnzvfs:/;

// The editor's label function (editor.js `t`), handed in by initPreview. Until then an id shows itself.
let t = (id) => id;

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
// Optional-chained calls count too: nnz?.on?.('follow').
const SUBSCRIPTION_PATTERN = /\.on\??\.?\(\s*['"]([a-zA-Z0-9_.:-]+)['"]/g;
const NON_WIDGET_EVENTS = new Set(['message', 'error']);

function extensionOf(path) {
    const dot = path.lastIndexOf('.');
    return dot === -1 ? '' : path.slice(dot + 1).toLowerCase();
}

// The overlay SDK as the preview runs it (preview-sdk.js): the live surface with no hub behind it. Fetched
// once per page and inlined into every widget frame, because a sandboxed srcdoc frame cannot load it itself.
const PREVIEW_SDK_URL = new URL('./preview-sdk.js', import.meta.url);

function loadPreviewSdk() {
    globalThis.__nnzPreviewSdk ??= fetch(PREVIEW_SDK_URL).then((response) => {
        if (!response.ok) throw new Error(t('previewSdkHttp', { status: response.status }));
        return response.text();
    });
    return globalThis.__nnzPreviewSdk;
}

// The globals the overlay host page injects before the SDK and the bundle run (OverlayHostController).
function widgetGlobals(widget, events) {
    const values = {
        WIDGET_ID: widget.id ?? 'preview',
        WIDGET_TOKEN: 'preview',
        WIDGET_NAME: widget.name ?? t('previewWidgetName'),
        WIDGET_SETTINGS: widget.settings ?? {},
        WIDGET_EVENT_SUBSCRIPTIONS: events,
    };
    return Object.entries(values)
        .map(([name, value]) => `window.${name}=${JSON.stringify(value).replace(/</g, '\\u003c')};`)
        .join('');
}

// Inline <script> text must never contain its own closing tag.
function inlineScript(source) {
    return `<script>${source.replace(/<\/script/gi, '<\\/script')}<\/script>`;
}

export function initPreview({
    frame,
    note,
    errorBox,
    onReveal,
    onEditSample,
    onHostAction,
    fireBar,
    refresh,
    language,
    entry,
    log,
    consolePanel,
    fireSamples = {},
    fireSamplesFailed = false,
    declaredEvents = [],
    widget = {},
    noteText = '',
    snapshotFiles,
    t: label,
}) {
    t = label;
    const framework = String(language ?? '').toLowerCase();

    const isVue = framework === 'vue';
    const entryExtension = extensionOf(entry);
    const mode = framework === 'script'
        ? 'note'
        : isVue
          ? 'esbuild'
          : entryExtension === 'html' || entryExtension === 'htm'
            ? 'html'
            : 'esbuild';

    const idleNote = noteText || (mode === 'note' ? t('previewScriptNote') : '');

    let esbuild = null;
    let vueSfc = null;
    let previewSdk = null;
    let timer = 0;

    function showNote(text, isError) {
        frame.hidden = true;
        fireBar.hidden = true;
        note.hidden = false;
        note.dataset.error = String(Boolean(isError));
        note.textContent = text;
    }

    // ── Error overlay: a failed build or a runtime error, over the preview ──

    const errorMain = errorBox.querySelector('.preview-error-main');
    const errorToggle = errorBox.querySelector('.preview-error-toggle');
    const errorStack = errorBox.querySelector('.preview-error-stack');
    const errorWhere = errorBox.querySelector('.preview-error-where');
    let errorTarget = null;

    function hideError() {
        errorBox.hidden = true;
        errorTarget = null;
    }

    function showError({ title, message, file, line, details }) {
        errorTarget = file && line ? { file, line } : null;
        errorBox.querySelector('.preview-error-title').textContent = title;
        errorBox.querySelector('.preview-error-message').textContent = message;
        errorWhere.hidden = errorTarget === null;
        errorWhere.textContent = errorTarget ? t('previewErrorLine', errorTarget) : '';
        errorStack.textContent = details ?? '';
        errorStack.hidden = true;
        errorToggle.hidden = !details;
        errorToggle.setAttribute('aria-expanded', 'false');
        errorBox.hidden = false;
    }

    errorMain.addEventListener('click', () => {
        if (errorTarget) onReveal(errorTarget.file, errorTarget.line);
    });
    errorToggle.addEventListener('click', () => {
        errorStack.hidden = !errorStack.hidden;
        errorToggle.setAttribute('aria-expanded', String(!errorStack.hidden));
    });

    // Where the last rendered document puts the author's code, so a stack position can be mapped back:
    // the document lines before it, and (for a bundle) the bundle's source map and the per-file Vue maps.
    let lastRender = { lineOffset: 0, bundleMap: null };
    let vueMaps = new Map();
    // Filled while a build runs; it replaces vueMaps only when that build succeeds and renders.
    let pendingVueMaps = new Map();

    function newlinesIn(text) {
        return text.split('\n').length - 1;
    }

    // The first stack frame inside the widget document that lands in the author's own files.
    function locateInStack(stack, files) {
        for (const text of String(stack ?? '').split('\n')) {
            const match = /srcdoc:(\d+):(\d+)/.exec(text);
            if (!match) continue;

            const line = Number(match[1]) - lastRender.lineOffset;
            if (line < 1) continue;

            if (!lastRender.bundleMap) return { file: entry, line };

            const position = lastRender.bundleMap.lookup(line, Number(match[2]) - 1);
            const file = position?.source.replace(VFS_PREFIX, '');
            if (!file || !(file in files)) continue;

            const original = vueMaps.get(file)?.lookup(position.line, position.column);
            return { file, line: original?.line ?? position.line };
        }
        return null;
    }

    function showRuntimeError(report) {
        const where = locateInStack(report.stack, snapshotFiles());
        showError({
            title: t('previewErrorRunTitle'),
            message: report.message ?? t('previewLogError'),
            file: where?.file,
            line: where?.line,
            details: report.stack,
        });
    }

    function showBuildError(error) {
        const first = error?.errors?.[0];
        const file = first?.location?.file?.replace(VFS_PREFIX, '');
        note.hidden = true;
        frame.hidden = false;
        showError({
            title: t('previewErrorBuildTitle'),
            message: first?.text ?? error?.message ?? String(error),
            file,
            line: first?.location?.line,
            details: error?.message,
        });
    }

    function showFrame(srcdoc) {
        clearLog();
        clearConsole();
        hideError();
        note.hidden = true;
        frame.hidden = false;
        frame.srcdoc = srcdoc;
    }

    // ── Fire bar ───────────────────────────────────────────────────────────

    // The widget's PERSISTED EventSubscriptions is the same list the overlay manifest reads at runtime — the
    // authoritative source, never able to drift the way scanning source text can (a computed event name, a
    // destructured `on`, or an unrelated `.on(...)` call from another library all confuse the scan). Prefer it
    // whenever it is non-empty; fall back to the source scan only for a widget with no declared subscriptions
    // yet (a brand-new custom widget — S062 tracks making this list itself editable in the dashboard).
    function subscribedEvents(files) {
        if (declaredEvents.length > 0) return [...declaredEvents];

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

    function sdkScripts(files) {
        return inlineScript(widgetGlobals(widget, subscribedEvents(files))) + inlineScript(previewSdk);
    }

    const fireSearch = fireBar.querySelector('#fireSearch');
    const fireList = fireBar.querySelector('#fireList');
    const fireEmpty = fireBar.querySelector('#fireEmpty');
    const fireSamplesError = fireBar.querySelector('#fireSamplesError');
    let listedKey = '';
    let editingType = null;

    // rewardId, reward_id and RewardId name the same thing.
    const idKeyOf = (key) => String(key).replace(/[_\-\s]/g, '').toLowerCase();

    // A widget that filters on one of its own settings (rewardId, ...) would ignore a sample carrying a made-up id,
    // so an id field of the sample takes the value of the setting that names the same id.
    function sampleFor(type) {
        const sample = fireSamples[type] ?? fireSamples._default ?? {};
        const settings = widget.settings ?? {};
        const settingIds = new Map(
            Object.entries(settings)
                .filter(([key, value]) => idKeyOf(key).endsWith('id') && ['string', 'number'].includes(typeof value) && value !== '')
                .map(([key, value]) => [idKeyOf(key), value]),
        );
        if (settingIds.size === 0 || typeof sample !== 'object' || sample === null || Array.isArray(sample)) return sample;
        const merged = { ...sample };
        for (const field of Object.keys(sample)) {
            if (settingIds.has(idKeyOf(field))) merged[field] = settingIds.get(idKeyOf(field));
        }
        return merged;
    }

    function postFire(type, data) {
        addLogEntry({ kind: 'fired', type });
        // '*' rather than the origin: a sandboxed frame without allow-same-origin has an opaque origin, which
        // matches no origin string at all.
        frame.contentWindow?.postMessage({ __nnzFire: { type, data } }, '*');
    }

    // Without the server's samples every payload would be empty, so nothing is fired at all.
    function fireEvent(type) {
        if (fireSamplesFailed) return;
        postFire(type, sampleFor(type));
    }

    // The widget's own events first (what it listens to), then every other event the server can send.
    function listedEventTypes(files) {
        const own = subscribedEvents(files);
        const rest = Object.keys(fireSamples)
            .filter((type) => type !== '_default' && !own.includes(type))
            .sort((a, b) => a.localeCompare(b));
        return [...own, ...rest];
    }

    function applyFireSearch() {
        const query = fireSearch.value.trim().toLowerCase();
        let shown = 0;
        for (const row of fireList.children) {
            const match = row.dataset.type.toLowerCase().includes(query);
            row.hidden = !match;
            if (match) shown += 1;
        }
        fireEmpty.hidden = shown > 0;
    }

    function markEditingRow() {
        for (const row of fireList.children) row.dataset.editing = String(row.dataset.type === editingType);
    }

    // The data itself is edited in a Monaco tab of the main editor; this bar only marks which event has one open.
    function openFireEditor(type) {
        setEditingType(type);
        onEditSample(type, sampleFor(type));
    }

    function setEditingType(type) {
        editingType = type;
        markEditingRow();
    }

    fireSearch.addEventListener('input', applyFireSearch);

    function fireRow(type) {
        const row = document.createElement('div');
        row.className = 'fire-row';
        row.dataset.type = type;

        const fire = document.createElement('button');
        fire.type = 'button';
        fire.className = 'fire-btn';
        fire.textContent = type;
        fire.disabled = fireSamplesFailed;
        fire.addEventListener('click', () => fireEvent(type));

        const edit = document.createElement('button');
        edit.type = 'button';
        edit.classList.add('btn', 'btn-quiet', 'fire-edit');
        edit.textContent = t('previewFireEdit');
        edit.disabled = fireSamplesFailed;
        edit.setAttribute('aria-label', t('previewFireEditSample', { type }));
        edit.addEventListener('click', () => openFireEditor(type));

        row.append(fire, edit);
        return row;
    }

    function refreshFireBar(files) {
        fireSamplesError.textContent = fireSamplesFailed ? t('previewFireSamplesError') : '';
        fireSamplesError.hidden = !fireSamplesFailed;
        const types = listedEventTypes(files);
        const key = types.join('|');
        if (key !== listedKey) {
            listedKey = key;
            fireList.replaceChildren(...types.map(fireRow));
            markEditingRow();
            applyFireSearch();
        }
        fireBar.hidden = false;
    }

    // ── Preview log: what the widget did that would reach the bot ───────────

    function describeEntry(entry) {
        switch (entry.kind) {
            case 'fired':
                return t('previewLogFired', { type: entry.type });
            case 'action':
                return t('previewLogAction', {
                    actionType: entry.actionType,
                    params: JSON.stringify(entry.params ?? {}),
                });
            case 'claim':
                return t('previewLogClaim', { key: entry.key });
            default:
                return entry.message ?? t('previewLogError');
        }
    }

    function addLogEntry(entry) {
        if (!log) return;
        const row = document.createElement('li');
        row.className = 'preview-log-row';
        row.dataset.kind = entry.kind;
        row.textContent = describeEntry(entry);
        log.append(row);
        log.hidden = false;
        row.scrollIntoView({ block: 'nearest' });
    }

    function clearLog() {
        if (!log) return;
        log.replaceChildren();
        log.hidden = true;
    }

    // ── Console: every console call of the widget, as one row each ──────────

    const CONSOLE_LEVELS = {
        log: 'consoleLevelLog',
        info: 'consoleLevelInfo',
        warn: 'consoleLevelWarn',
        error: 'consoleLevelError',
        debug: 'consoleLevelDebug',
    };
    const CONSOLE_MAX_ROWS = 500;

    function consoleCell(tag, className, text) {
        const cell = document.createElement(tag);
        cell.className = className;
        cell.textContent = text;
        return cell;
    }

    // `where` is `file:line` when known; `at` is a millisecond timestamp, now when absent.
    function addConsoleRow({ level, text, where, at }) {
        if (!consolePanel) return;
        const known = level in CONSOLE_LEVELS ? level : 'log';
        const row = document.createElement('li');
        row.className = 'console-row';
        row.dataset.level = known;
        row.dataset.text = text;
        row.append(
            consoleCell('time', 'console-time', new Date(at ?? Date.now()).toLocaleTimeString([], { hour12: false })),
            consoleCell('span', 'console-level', t(CONSOLE_LEVELS[known])),
            consoleCell('span', 'console-text', text),
            consoleCell('span', 'console-source', where ?? ''),
        );
        consolePanel.list.append(row);
        while (consolePanel.list.childElementCount > CONSOLE_MAX_ROWS) consolePanel.list.firstElementChild.remove();
        consolePanel.list.hidden = false;
        consolePanel.empty.hidden = true;
        row.scrollIntoView({ block: 'nearest' });
    }

    function clearConsole() {
        if (!consolePanel) return;
        consolePanel.list.replaceChildren();
        consolePanel.list.hidden = true;
        consolePanel.empty.hidden = false;
    }

    consolePanel?.clear.addEventListener('click', clearConsole);

    function showConsoleEntry(entry) {
        const where = locateInStack(entry.stack, snapshotFiles());
        addConsoleRow({
            level: entry.level,
            text: String(entry.text ?? ''),
            where: where ? `${where.file}:${where.line}` : '',
            at: entry.at,
        });
    }

    window.addEventListener('message', (event) => {
        if (event.source !== frame.contentWindow) return;
        const entry = event.data?.__nnzPreview;
        if (entry?.kind === 'console') {
            showConsoleEntry(entry);
            return;
        }
        if (entry?.kind === 'previewAction') {
            onHostAction?.(entry);
            return;
        }
        if (entry && typeof entry === 'object') {
            addLogEntry(entry);
            if (entry.kind === 'error') showRuntimeError(entry);
        }
    });

    // ── Rendering ──────────────────────────────────────────────────────────

    function renderBundle(files, javascript, css, bundleMap) {
        const reset =
            'html,body{margin:0;padding:0;background:transparent;color:#e5e5e5;font-family:-apple-system,BlinkMacSystemFont,sans-serif;}';
        const sdk = sdkScripts(files);
        const before =
            '<!doctype html><html><head><meta charset="utf-8">' +
            `<style>${reset}</style><style>${css ?? ''}</style>` +
            `<script type="importmap">${JSON.stringify(IMPORT_MAP)}<\/script></head><body>` +
            `<div id="app"></div><div id="root"></div>${sdk}` +
            '<script type="module">';
        showFrame(`${before}${javascript}<\/script></body></html>`);
        lastRender = { lineOffset: newlinesIn(before), bundleMap };
        refreshFireBar(files);
    }

    function renderHtmlDirect() {
        const files = snapshotFiles();
        const html = files[entry] ?? '';
        const sdk = sdkScripts(files);
        // The SDK must exist before the widget's own scripts, so it goes in as early as the page allows.
        const head = /<head[^>]*>/i;
        showFrame(head.test(html) ? html.replace(head, (tag) => tag + sdk) : sdk + html);
        lastRender = { lineOffset: newlinesIn(sdk), bundleMap: null };
        refreshFireBar(files);
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
        if (parsed.errors?.length) {
            const problem = parsed.errors[0];
            throw Object.assign(new Error(problem.message ?? String(problem)), { line: problem.loc?.start?.line });
        }

        const descriptor = parsed.descriptor;
        if (!descriptor.scriptSetup && !descriptor.script) throw new Error(t('previewSfcNoScript'));

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

        // The compiled script's lines are not the author's: keep the map so a stack line can be taken back.
        const scriptMap = createSourceMap(compiled.map);
        if (scriptMap) pendingVueMaps.set(path, scriptMap);

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
        const mountErrorTemplate = JSON.stringify(t('previewMountError')).replace(/</g, '\\u003c');
        return (
            `import __App from "./${entry}";\n` +
            'import { createApp } from "vue";\n' +
            // Vue hands render and setup errors to this handler instead of logging them, so it reports them.
            'try { window.__nnzApp = createApp(__App); window.__nnzApp.config.errorHandler = function (e) { window.NomNomz.reportError(e); }; window.__nnzApp.mount("#app"); }\n' +
            `catch (e) { window.NomNomz.reportError(e); var d = document.getElementById("app"); if (d) { d.textContent = ${mountErrorTemplate}.replace("{message}", (e && e.message) || e); d.style.color = "#f87171"; } }`
        );
    }

    // A position esbuild can show: the file and the author's own line.
    function lineLocation(source, file, line) {
        const lineText = source.split('\n')[line - 1] ?? '';
        return { file, line, column: 0, lineText };
    }

    // The first line of `file` that mentions `needle` — where an import of it was written.
    function locateText(files, file, needle) {
        const source = files[file] ?? '';
        const index = source.split('\n').findIndex((text) => text.includes(needle));
        return index === -1 ? undefined : lineLocation(source, file, index + 1);
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
                            : {
                                  errors: [
                                      {
                                          text: t('previewCannotResolve', { path: args.path, importer: args.importer }),
                                          location: locateText(files, args.importer, args.path),
                                      },
                                  ],
                              };
                    }
                    return { path: args.path, external: true };
                });

                build.onLoad({ filter: /.*/, namespace: 'nnzvfs' }, (args) => {
                    if (args.path === VUE_ENTRY) return { contents: vueEntrySource(), loader: 'js' };

                    const contents = files[args.path];
                    if (contents == null) return { errors: [{ text: t('previewMissingFile', { path: args.path }) }] };

                    if (args.path.endsWith('.vue')) {
                        try {
                            return { contents: compileVueFile(args.path, contents), loader: 'ts' };
                        } catch (error) {
                            return {
                                errors: [
                                    {
                                        text: t('previewVueCompile', { path: args.path, message: error?.message ?? error }),
                                        location: error?.line ? lineLocation(contents, args.path, error.line) : undefined,
                                    },
                                ],
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
            showNote(t('previewEntryMissing', { entry }), true);
            return;
        }

        if (isVue && !vueSfc) {
            showNote(t('previewLoadingVue'), false);
            try {
                vueSfc = await loadVueSfc();
            } catch (error) {
                // Drop the cached rejection so the next edit retries instead of inheriting the failure.
                globalThis.__nnzVueSfc = null;
                showNote(`${t('previewVueLoadFailed')}\n${error?.message ?? error}`, true);
                return;
            }
        }

        pendingVueMaps = new Map();
        try {
            const result = await esbuild.build({
                entryPoints: [isVue ? VUE_ENTRY : entry],
                bundle: true,
                write: false,
                sourcemap: 'external',
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
            const mapText = result.outputFiles.find((file) => file.path.endsWith('.js.map'))?.text;
            vueMaps = pendingVueMaps;
            renderBundle(files, javascript, css, mapText ? createSourceMap(JSON.parse(mapText)) : null);
        } catch (error) {
            // The last good frame stays under the message, so a typo does not blank the widget.
            showBuildError(error);
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
        if (mode === 'note') {
            showNote(idleNote, false);
            return;
        }
        if (previewSdk === null) return; // the first render runs once the SDK has loaded
        if (mode === 'esbuild') buildBundle();
        else renderHtmlDirect();
    }

    function schedule() {
        if (mode !== 'esbuild' && mode !== 'html') return;
        clearTimeout(timer);
        timer = setTimeout(rebuildNow, REBUILD_DEBOUNCE_MS);
    }

    refresh.addEventListener('click', rebuildNow);

    if (mode === 'note') {
        rebuildNow();
    } else {
        showNote(t('previewStarting'), false);
        Promise.all([loadPreviewSdk(), mode === 'esbuild' ? loadEsbuild() : null])
            .then(([sdk, loaded]) => {
                previewSdk = sdk;
                esbuild = loaded;
                rebuildNow();
            })
            .catch((error) => {
                // Drop the cached rejections so a later open can retry rather than inherit the failure.
                globalThis.__nnzEsbuild = null;
                globalThis.__nnzPreviewSdk = null;
                showNote(
                    `${t('previewUnavailable')}\n${error?.message ?? error}\n\n` + t('previewUnavailableHint'),
                    true,
                );
            });
    }

    // The host's answer to a previewAction goes back down into the frame that asked ('*': opaque sandbox origin).
    function replyHostAction(result) {
        frame.contentWindow?.postMessage({ __nnzPreviewActionResult: result }, '*');
    }

    return { mode, schedule, rebuildNow, addConsoleRow, clearConsole, fire: postFire, setEditingType, replyHostAction };
}
