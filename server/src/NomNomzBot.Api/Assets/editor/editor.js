// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The code editor, as a real page.
//
// It runs in an iframe owned by the dashboard. The dashboard is Compose/Wasm and paints to a canvas, so it
// cannot host a DOM component directly; this page is where the DOM lives. Monaco therefore loads into an
// ordinary document, which is why its own stylesheet works here with no shadow-root workaround.
//
// The host hands over the project and receives the edited files back over postMessage. Nothing here talks to
// the API — the dashboard already holds the session, so this page needs no token of its own.

import { initPreview } from './preview.js';

const HOST_MESSAGE = Object.freeze({
    open: 'nnz:editor:open',
    ready: 'nnz:editor:ready',
    save: 'nnz:editor:save',
    compiled: 'nnz:editor:compiled',
    close: 'nnz:editor:close',
});

// Pinned, and single-sourced: the AMD loader, the module root and the stylesheet must never drift apart.
// Overridable via `data-monaco-base` on <html> so a deployment can serve Monaco from its own origin instead
// of a public CDN. Read off the root element, not `document.currentScript` — that is always null in a module.
const MONACO_BASE =
    document.documentElement.dataset.monacoBase ??
    'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs';

const LANGUAGE_BY_EXTENSION = Object.freeze({
    ts: 'typescript',
    tsx: 'typescript',
    js: 'javascript',
    jsx: 'javascript',
    mjs: 'javascript',
    cjs: 'javascript',
    json: 'json',
    css: 'css',
    html: 'html',
    htm: 'html',
    vue: 'html',
});

const dom = {
    shell: document.getElementById('shell'),
    boot: document.getElementById('boot'),
    bootMessage: document.getElementById('bootMessage'),
    title: document.getElementById('title'),
    kind: document.getElementById('kind'),
    result: document.getElementById('result'),
    fileList: document.getElementById('fileList'),
    tabs: document.getElementById('tabs'),
    editorHost: document.getElementById('editorHost'),
    problems: document.getElementById('problems'),
    cursor: document.getElementById('cursor'),
    language: document.getElementById('language'),
    problemCount: document.getElementById('problemCount'),
    save: document.getElementById('save'),
    close: document.getElementById('close'),
    newFile: document.getElementById('newFile'),
    togglePreview: document.getElementById('togglePreview'),
    format: document.getElementById('format'),
    wrap: document.getElementById('wrap'),
    minimap: document.getElementById('minimap'),
    previewFrame: document.getElementById('previewFrame'),
    previewNote: document.getElementById('previewNote'),
    fireBar: document.getElementById('fireBar'),
    refresh: document.getElementById('refresh'),
};

const state = {
    files: new Map(),
    entry: '',
    active: '',
    models: new Map(),
    editor: null,
    monaco: null,
    preview: null,
    wrap: false,
    minimap: true,
};

// ── Host bridge ────────────────────────────────────────────────────────────

function postToHost(message) {
    // Same-origin iframe: the host is the dashboard that served this page.
    window.parent?.postMessage(message, window.location.origin);
}

function extensionOf(path) {
    const dot = path.lastIndexOf('.');
    return dot === -1 ? '' : path.slice(dot + 1).toLowerCase();
}

function languageOf(path) {
    return LANGUAGE_BY_EXTENSION[extensionOf(path)] ?? 'plaintext';
}

// ── Files ──────────────────────────────────────────────────────────────────

function normalizePath(raw) {
    return String(raw)
        .split('/')
        .filter((segment) => segment.length > 0)
        .join('/')
        .trim();
}

function flushActive() {
    const model = state.models.get(state.active);
    if (model) state.files.set(state.active, model.getValue());
}

// What the preview bundles: the saved map with the file being typed in folded back in.
function snapshotFiles() {
    flushActive();
    return Object.fromEntries(state.files);
}

function modelFor(path) {
    const existing = state.models.get(path);
    if (existing) return existing;

    // A real file:/// URI is what lets the TypeScript worker resolve `./helper` to a sibling model.
    // Models created without one get inmemory://model/N, which no relative specifier can ever name.
    const uri = state.monaco.Uri.parse(`file:///${path}`);
    const model =
        state.monaco.editor.getModel(uri) ??
        state.monaco.editor.createModel(state.files.get(path) ?? '', languageOf(path), uri);
    model.onDidChangeContent(() => state.preview?.schedule());
    state.models.set(path, model);
    return model;
}

function selectFile(path) {
    if (!state.files.has(path)) return;
    flushActive();
    state.active = path;
    state.editor.setModel(modelFor(path));
    state.editor.focus();
    renderFiles();
    renderTabs();
}

function addFile() {
    const name = normalizePath(window.prompt('New file path (e.g. lib/helper.ts)') ?? '');
    if (!name || state.files.has(name)) return;
    flushActive();
    state.files.set(name, '');
    selectFile(name);
    state.preview?.schedule();
}

function renameFile(path) {
    if (path === state.entry) return;
    const next = normalizePath(window.prompt('Rename file', path) ?? '');
    if (!next || next === path || state.files.has(next)) return;
    flushActive();
    state.files.set(next, state.files.get(path) ?? '');
    state.files.delete(path);
    disposeModel(path);
    if (state.active === path) state.active = next;
    selectFile(state.active);
    state.preview?.schedule();
}

function deleteFile(path) {
    if (path === state.entry) return;
    if (!window.confirm(`Delete ${path}? This cannot be undone until you close without saving.`)) return;
    state.files.delete(path);
    disposeModel(path);
    if (state.active === path) state.active = state.entry;
    selectFile(state.active);
    state.preview?.schedule();
}

function disposeModel(path) {
    state.models.get(path)?.dispose();
    state.models.delete(path);
}

function renderFiles() {
    dom.fileList.replaceChildren(
        ...[...state.files.keys()].sort().map((path) => {
            const row = document.createElement('button');
            row.type = 'button';
            row.className = 'file-row';
            row.setAttribute('aria-current', String(path === state.active));
            row.addEventListener('click', () => selectFile(path));

            const name = document.createElement('span');
            name.className = 'file-name';
            name.textContent = path;
            row.append(name);

            if (path === state.entry) {
                const marker = document.createElement('span');
                marker.className = 'file-entry';
                marker.textContent = '•';
                marker.title = 'Entry file';
                row.append(marker);
            } else {
                row.append(
                    fileAction('Rename', (event) => {
                        event.stopPropagation();
                        renameFile(path);
                    }),
                    fileAction('Delete', (event) => {
                        event.stopPropagation();
                        deleteFile(path);
                    }),
                );
            }

            const item = document.createElement('li');
            item.append(row);
            return item;
        }),
    );
}

function fileAction(label, onClick) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'file-action';
    button.textContent = label;
    button.title = label;
    button.addEventListener('click', onClick);
    return button;
}

function renderTabs() {
    dom.tabs.replaceChildren(
        ...[...state.files.keys()].sort().map((path) => {
            const tab = document.createElement('button');
            tab.type = 'button';
            tab.role = 'tab';
            tab.className = 'tab';
            tab.setAttribute('aria-selected', String(path === state.active));
            tab.title = path;
            tab.textContent = path.split('/').pop();
            tab.addEventListener('click', () => selectFile(path));
            return tab;
        }),
    );
}

// ── Monaco ─────────────────────────────────────────────────────────────────

function loadMonaco() {
    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = `${MONACO_BASE}/loader.js`;
        script.onload = () => {
            const amd = window.require;
            if (!amd?.config) {
                reject(new Error('Monaco AMD loader did not install require()'));
                return;
            }
            amd.config({ paths: { vs: MONACO_BASE } });
            amd(['vs/editor/editor.main'], () => resolve(window.monaco), reject);
        };
        script.onerror = () => reject(new Error('Monaco loader script failed to load'));
        document.head.append(script);
    });
}

function configureLanguageServices(monaco, sdkTypes) {
    const ts = monaco.languages.typescript;
    if (!ts) return;

    const compilerOptions = {
        target: ts.ScriptTarget.ESNext,
        module: ts.ModuleKind.ESNext,
        moduleResolution: ts.ModuleResolutionKind.NodeJs,
        allowJs: true,
        allowNonTsExtensions: true,
        noEmit: true,
        skipLibCheck: true,
        lib: ['esnext', 'dom'],
    };

    // A file is highlighted as javascript or typescript purely by extension; the SDK must behave the same
    // either way, so both services get identical treatment.
    for (const service of [ts.javascriptDefaults, ts.typescriptDefaults]) {
        service.setCompilerOptions(compilerOptions);
        service.setDiagnosticsOptions({ noSemanticValidation: false, noSyntaxValidation: false });
        service.setEagerModelSync(true);
        if (sdkTypes) service.addExtraLib(sdkTypes, 'file:///nnz-sdk.d.ts');
    }
}

function createEditor(monaco) {
    return monaco.editor.create(dom.editorHost, {
        model: modelFor(state.active),
        theme: 'vs-dark',
        automaticLayout: true,
        minimap: { enabled: true, renderCharacters: false, maxColumn: 80 },
        fontFamily: 'var(--font-code)',
        fontSize: 13,
        fontLigatures: true,
        lineHeight: 20,
        scrollBeyondLastLine: false,
        smoothScrolling: true,
        cursorBlinking: 'smooth',
        cursorSmoothCaretAnimation: 'on',
        renderLineHighlight: 'all',
        bracketPairColorization: { enabled: true },
        guides: { bracketPairs: true, indentation: true },
        stickyScroll: { enabled: true },
        folding: true,
        linkedEditing: true,
        formatOnPaste: true,
        suggestOnTriggerCharacters: true,
        quickSuggestions: { other: true, comments: false, strings: false },
        parameterHints: { enabled: true },
        inlayHints: { enabled: 'on' },
        occurrencesHighlight: 'singleFile',
        multiCursorModifier: 'ctrlCmd',
        tabSize: 2,
        rulers: [100],
        padding: { top: 10, bottom: 10 },
    });
}

// ── Problems + status ──────────────────────────────────────────────────────

const SEVERITY = new Map();

function severityName(monaco, severity) {
    if (SEVERITY.size === 0) {
        SEVERITY.set(monaco.MarkerSeverity.Error, 'error');
        SEVERITY.set(monaco.MarkerSeverity.Warning, 'warning');
        SEVERITY.set(monaco.MarkerSeverity.Info, 'info');
        SEVERITY.set(monaco.MarkerSeverity.Hint, 'info');
    }
    return SEVERITY.get(severity) ?? 'info';
}

function renderProblems(monaco) {
    const markers = monaco.editor.getModelMarkers({});
    const errors = markers.filter((m) => m.severity === monaco.MarkerSeverity.Error).length;
    const warnings = markers.filter((m) => m.severity === monaco.MarkerSeverity.Warning).length;

    dom.problems.replaceChildren(
        ...markers.map((marker) => {
            const file = String(marker.resource?.path ?? '').replace(/^\/+/, '');
            const row = document.createElement('button');
            row.type = 'button';
            row.className = 'problem';
            row.addEventListener('click', () => {
                if (file && file !== state.active && state.files.has(file)) selectFile(file);
                state.editor.revealLineInCenter(marker.startLineNumber);
                state.editor.setPosition({
                    lineNumber: marker.startLineNumber,
                    column: marker.startColumn,
                });
                state.editor.focus();
            });

            const severity = document.createElement('span');
            severity.className = 'problem-severity';
            severity.dataset.severity = severityName(monaco, marker.severity);
            severity.textContent = severity.dataset.severity;

            const where = document.createElement('span');
            where.className = 'problem-where';
            where.textContent = `${file}:${marker.startLineNumber}`;

            const message = document.createElement('span');
            message.className = 'problem-message';
            message.textContent = marker.message;

            row.append(severity, where, message);
            return row;
        }),
    );

    const total = errors + warnings;
    dom.problemCount.textContent =
        total === 0
            ? 'No problems'
            : `${errors} error${errors === 1 ? '' : 's'}, ${warnings} warning${warnings === 1 ? '' : 's'}`;
    dom.problemCount.dataset.severity = errors > 0 ? 'error' : warnings > 0 ? 'warning' : 'none';
    if (total === 0) dom.problems.hidden = true;
}

function syncStatus() {
    const position = state.editor.getPosition();
    if (position) dom.cursor.textContent = `Ln ${position.lineNumber}, Col ${position.column}`;
    dom.language.textContent = state.editor.getModel()?.getLanguageId() ?? '';
}

// ── Preview ────────────────────────────────────────────────────────────────

function setPreviewCollapsed(collapsed) {
    dom.shell.dataset.preview = collapsed ? 'collapsed' : 'open';
    dom.togglePreview.textContent = collapsed ? 'Show preview' : 'Hide preview';
    // Widening the editor while it was scrolled right leaves a stale scrollLeft, which renders every line
    // with its opening characters cut off.
    state.editor?.layout();
    state.editor?.setScrollLeft(0);
}

function installSplitter() {
    const splitter = document.getElementById('splitter');
    const preview = document.getElementById('preview');
    const body = document.querySelector('.body');

    splitter.addEventListener('mousedown', (down) => {
        down.preventDefault();
        const onMove = (move) => {
            const rect = body.getBoundingClientRect();
            const max = rect.width - 420;
            const next = Math.min(Math.max(rect.right - move.clientX, 260), Math.max(max, 260));
            preview.style.flexBasis = `${next}px`;
            state.editor?.layout();
        };
        const onUp = () => {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
        };
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    });
}

// ── Save / close ───────────────────────────────────────────────────────────

function requestSave() {
    if (dom.save.disabled) return;
    flushActive();
    dom.save.disabled = true;
    dom.save.textContent = 'Compiling…';
    dom.result.hidden = true;
    postToHost({ type: HOST_MESSAGE.save, files: Object.fromEntries(state.files) });
}

function showCompileResult({ ok, message }) {
    dom.result.hidden = false;
    dom.result.dataset.ok = String(Boolean(ok));
    dom.result.textContent = message ?? '';
    dom.save.disabled = false;
    dom.save.textContent = 'Save & Compile';
}

// ── Boot ───────────────────────────────────────────────────────────────────

async function open(payload) {
    state.files = new Map(Object.entries(payload.files ?? {}));
    state.entry = payload.entry ?? [...state.files.keys()][0] ?? 'index.ts';
    if (!state.files.has(state.entry)) state.files.set(state.entry, '');
    state.active = state.entry;

    dom.title.textContent = payload.title ?? 'Editor';
    dom.kind.textContent = payload.language ?? '';
    if (payload.accent) document.documentElement.style.setProperty('--accent', payload.accent);

    installSplitter();

    // Started before Monaco: the two toolchains load in parallel, and a Monaco failure still leaves a
    // working preview (and vice versa).
    state.preview = initPreview({
        frame: dom.previewFrame,
        note: dom.previewNote,
        fireBar: dom.fireBar,
        refresh: dom.refresh,
        language: payload.language ?? '',
        entry: state.entry,
        fireSamples: payload.fireSamples ?? {},
        noteText: payload.previewNote ?? '',
        snapshotFiles,
    });

    // Nothing renders for a code script, so its preview pane would be dead width. The preview owns that
    // judgement — 'note' is exactly "this project has nothing to show".
    setPreviewCollapsed(state.preview.mode === 'note');

    const monaco = await loadMonaco();
    state.monaco = monaco;
    configureLanguageServices(monaco, payload.sdkTypes ?? '');

    // Register every file up front, not lazily: the language service only sees files it has a model for, so
    // a helper the author has not clicked into would otherwise be invisible to cross-file resolution.
    for (const path of state.files.keys()) modelFor(path);

    state.editor = createEditor(monaco);
    state.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, requestSave);
    state.editor.onDidChangeCursorPosition(syncStatus);
    state.editor.onDidChangeModel(syncStatus);
    monaco.editor.onDidChangeMarkers(() => renderProblems(monaco));

    renderFiles();
    renderTabs();
    syncStatus();
    renderProblems(monaco);

    dom.boot.hidden = true;
    dom.shell.hidden = false;
    state.editor.focus();
}

function wireChrome() {
    dom.save.addEventListener('click', requestSave);
    dom.close.addEventListener('click', () => postToHost({ type: HOST_MESSAGE.close }));
    dom.newFile.addEventListener('click', addFile);
    dom.togglePreview.addEventListener('click', () =>
        setPreviewCollapsed(dom.shell.dataset.preview !== 'collapsed'),
    );
    dom.problemCount.addEventListener('click', () => {
        dom.problems.hidden = !dom.problems.hidden || dom.problems.childElementCount === 0;
    });
    dom.format.addEventListener('click', () => {
        state.editor?.getAction('editor.action.formatDocument')?.run();
        state.editor?.focus();
    });
    dom.wrap.addEventListener('click', () => {
        state.wrap = !state.wrap;
        state.editor?.updateOptions({ wordWrap: state.wrap ? 'on' : 'off' });
        if (state.wrap) state.editor?.setScrollLeft(0);
        dom.wrap.textContent = `Wrap: ${state.wrap ? 'on' : 'off'}`;
    });
    dom.minimap.addEventListener('click', () => {
        state.minimap = !state.minimap;
        state.editor?.updateOptions({
            minimap: { enabled: state.minimap, renderCharacters: false, maxColumn: 80 },
        });
        dom.minimap.textContent = `Minimap: ${state.minimap ? 'on' : 'off'}`;
    });
    window.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            postToHost({ type: HOST_MESSAGE.close });
        }
    });
}

window.addEventListener('message', (event) => {
    if (event.origin !== window.location.origin) return;
    const data = event.data;
    if (data?.type === HOST_MESSAGE.open) {
        open(data.payload).catch((error) => {
            dom.boot.hidden = false;
            dom.boot.dataset.error = 'true';
            dom.bootMessage.textContent = `The editor could not start: ${error.message}`;
        });
    } else if (data?.type === HOST_MESSAGE.compiled) {
        showCompileResult(data);
    }
});

wireChrome();
postToHost({ type: HOST_MESSAGE.ready });
