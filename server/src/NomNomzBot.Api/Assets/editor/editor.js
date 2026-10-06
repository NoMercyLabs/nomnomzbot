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
import { createVueScriptModels, isHiddenScriptResource } from './vue-script-model.js';

const HOST_MESSAGE = Object.freeze({
    open: 'nnz:editor:open',
    ready: 'nnz:editor:ready',
    save: 'nnz:editor:save',
    compiled: 'nnz:editor:compiled',
    close: 'nnz:editor:close',
    // Host to page: the host's own close control (the desktop window's X) asks; the page answers like Close.
    requestClose: 'nnz:editor:requestClose',
    // S-CODE-COLLAPSE: the History side view (version list, publish, delete) and the Test run panel folded
    // into Run & test — both optional, declared by the host per project (see `open()`'s `history` /
    // `testRunEnabled` payload fields).
    historyLoadMore: 'nnz:editor:historyLoadMore',
    historyRollback: 'nnz:editor:historyRollback',
    historyDelete: 'nnz:editor:historyDelete',
    historyPage: 'nnz:editor:historyPage',
    historyError: 'nnz:editor:historyError',
    testRun: 'nnz:editor:testRun',
    testRunResult: 'nnz:editor:testRunResult',
});

// Pinned, and single-sourced: the AMD loader, the module root and the stylesheet must never drift apart.
// Overridable via `data-monaco-base` on <html> so a deployment can serve Monaco from its own origin instead
// of a public CDN. Read off the root element, not `document.currentScript` — that is always null in a module.
const EDITOR_TAB_SIZE = 2;

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
    sdkTypesNotice: document.getElementById('sdkTypesNotice'),
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
    unsavedBackdrop: document.getElementById('unsavedBackdrop'),
    unsavedKeep: document.getElementById('unsavedKeep'),
    unsavedDiscard: document.getElementById('unsavedDiscard'),
    newFile: document.getElementById('newFile'),
    togglePreview: document.getElementById('togglePreview'),
    format: document.getElementById('format'),
    wrap: document.getElementById('wrap'),
    minimap: document.getElementById('minimap'),
    previewFrame: document.getElementById('previewFrame'),
    previewNote: document.getElementById('previewNote'),
    previewError: document.getElementById('previewError'),
    fireBar: document.getElementById('fireBar'),
    previewLog: document.getElementById('previewLog'),
    refresh: document.getElementById('refresh'),
    activity: document.getElementById('activity'),
    sidebar: document.getElementById('sidebar'),
    sidebarSplitter: document.getElementById('sidebarSplitter'),
    searchInput: document.getElementById('searchInput'),
    searchResults: document.getElementById('searchResults'),
    problemsSidebar: document.getElementById('problemsSidebar'),
    activityProblemBadge: document.getElementById('activityProblemBadge'),
    runTest: document.getElementById('runTest'),
    runNote: document.getElementById('runNote'),
    runSandboxHint: document.getElementById('runSandboxHint'),
    testRun: document.getElementById('testRun'),
    testRunTrigger: document.getElementById('testRunTrigger'),
    testRunTriggerLabel: document.getElementById('testRunTriggerLabel'),
    testRunRole: document.getElementById('testRunRole'),
    testRunRoleLabel: document.getElementById('testRunRoleLabel'),
    testRunVars: document.getElementById('testRunVars'),
    testRunArgs: document.getElementById('testRunArgs'),
    testRunButton: document.getElementById('testRunButton'),
    testRunStatus: document.getElementById('testRunStatus'),
    testRunResult: document.getElementById('testRunResult'),
    historyActivityItem: document.getElementById('historyActivityItem'),
    historyList: document.getElementById('historyList'),
    historyStatus: document.getElementById('historyStatus'),
    historyLoadMore: document.getElementById('historyLoadMore'),
    bundleMeta: document.getElementById('bundleMeta'),
    theme: document.getElementById('theme'),
    paletteBackdrop: document.getElementById('paletteBackdrop'),
    paletteInput: document.getElementById('paletteInput'),
    paletteList: document.getElementById('paletteList'),
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
    labels: null,
    // The files as the host last accepted them: what Close and Esc compare against before throwing edits away.
    savedFiles: new Map(),
    pendingSave: null,
};

// ── Words ──────────────────────────────────────────────────────────────────
// Every word the editor shows is a label id. The host (the dashboard) sends the translated map in the open
// payload (`labels`); this map is the single English fallback, and index.html's text must equal it. Test-run
// pickers (manual/trigger/role/timeline*) and `roles` come from the same payload and have no fallback here.
const DEFAULT_LABELS = Object.freeze({
    htmlLang: 'en',
    pageTitle: 'NomNomzBot — Code editor',
    defaultTitle: 'Editor',
    shortcutsHint: 'Ctrl+P files · F1 commands · Ctrl+S save',
    hidePreview: 'Hide preview',
    showPreview: 'Show preview',
    close: 'Close',
    unsavedChangesTitle: 'Unsaved changes',
    unsavedChangesBody: 'You have edits that are not saved. If you close now, they are lost.',
    keepEditing: 'Keep editing',
    discardChanges: 'Discard changes',
    saveAndCompile: 'Save & Compile',
    compiling: 'Compiling…',
    views: 'Views',
    explorer: 'Explorer',
    explorerTooltip: 'Explorer (Ctrl+Shift+E)',
    search: 'Search',
    searchTooltip: 'Search (Ctrl+Shift+F)',
    problems: 'Problems',
    problemsTooltip: 'Problems (Ctrl+Shift+M)',
    runAndTest: 'Run & test',
    runAndTestTooltip: 'Run & test (Ctrl+Shift+D)',
    history: 'History',
    historyTooltip: 'History (Ctrl+Shift+H)',
    bundle: 'Bundle',
    newFile: 'New file',
    searchInFiles: 'Search in files',
    runInSandbox: 'Run in sandbox',
    sandboxHint:
        'Runs what is in the editor right now, with sample events. Nothing is published and your live channel is never touched.',
    testRunVariables: 'Variables (one key=value per line)',
    testRunArguments: 'Arguments (space-separated)',
    runTest: 'Run test',
    running: 'Running…',
    whatWidgetDid: 'What the widget did',
    historyHint:
        'Every saved version, newest first. Publish any past version to make it the served one again; the current live version cannot be deleted.',
    loadMore: 'Load more',
    bundleHint: 'Bundle metadata for publishing this project.',
    sdkTypesNotice: 'SDK types could not load. Type checking is off until you reopen this file.',
    livePreview: 'Live preview',
    refresh: 'Refresh',
    widgetPreview: 'Widget preview',
    cursorPosition: 'Ln {line}, Col {column}',
    noProblems: 'No problems',
    theme: 'Theme',
    format: 'Format',
    wrapOn: 'Wrap: on',
    wrapOff: 'Wrap: off',
    minimapOn: 'Minimap: on',
    minimapOff: 'Minimap: off',
    commandPalette: 'Command palette',
    loadingEditor: 'Loading editor…',
    editorStartFailed: 'The editor could not start: {message}',
    newFilePrompt: 'New file path (e.g. lib/helper.ts)',
    renameFilePrompt: 'Rename file',
    deleteFileConfirm: 'Delete {path}? This cannot be undone until you close without saving.',
    entryFile: 'Entry file',
    rename: 'Rename',
    delete: 'Delete',
    severityError: 'error',
    severityWarning: 'warning',
    severityInfo: 'info',
    errorsOne: '{count} error',
    errorsMany: '{count} errors',
    warningsOne: '{count} warning',
    warningsMany: '{count} warnings',
    problemSummary: '{errors}, {warnings}',
    noProblemsDetected: 'No problems detected.',
    noMatchesFor: 'No matches for “{term}”.',
    noMatches: 'No matches.',
    themeLabel: 'Theme: {name}',
    themeDark: 'Dark',
    themeLight: 'Light',
    themeHighContrastDark: 'High contrast dark',
    themeHighContrastLight: 'High contrast light',
    themeAtomOneDark: 'Atom One Dark',
    themeGitHubLight: 'GitHub Light',
    themeDracula: 'Dracula',
    themeMonokai: 'Monokai',
    formatDocument: 'Format document',
    togglePreview: 'Toggle preview',
    toggleWordWrap: 'Toggle word wrap',
    toggleMinimap: 'Toggle minimap',
    viewLabel: 'View: {name}',
    historyEmpty: 'No saved versions yet.',
    versionLabel: 'v{version} — {status}',
    current: '(current)',
    publish: 'Publish',
    deleteVersionConfirm: 'Delete this saved version? This cannot be undone.',
    actionFailed: 'That action failed.',
    testRunFailed: 'Test run failed.',
    testRunSuccess: 'Success — {ms}ms, {calls} host call(s)',
    testRunFailedWith: 'Failed: {error}',
    testRunFailedPlain: 'Failed',
    variablesSet: 'Variables set',
    chatOutput: 'Chat output',
    capturedEffects: 'Captured effects',
    consoleOutput: 'Console',
    none: '(none)',
    runNoteSandbox:
        'This project runs in the bot sandbox, so there is nothing to render. Save & Compile validates it.',
    runNoteRender: 'Renders the current editor contents. Fire an event below to drive it.',
    bundleName: 'Name',
    bundleKind: 'Kind',
    bundleEntry: 'Entry',
    bundleFiles: 'Files',
    previewScriptNote: 'Code scripts run in the bot sandbox — press Save & Compile to validate.',
    previewFireEvent: 'Fire event:',
    previewLogFired: 'Fired {type}',
    previewFireSearch: 'Search events',
    previewFireNoMatch: 'No event matches your search.',
    previewFireClose: 'Close',
    previewFireJson: 'Event data as JSON',
    previewFireSend: 'Fire with this data',
    previewFireEdit: 'Edit',
    previewFireEditSample: 'Edit the data of {type}',
    previewFireEditing: 'Data for {type}',
    previewFireJsonInvalid: 'This is not valid JSON: {message}',
    previewLogAction: 'Would run {actionType} {params}',
    previewLogClaim: 'Claimed {key}',
    previewLogError: 'Error',
    previewMountError: 'Mount error: {message}',
    previewEntryMissing: 'Entry file {entry} is missing.',
    previewLoadingVue: 'Loading Vue compiler…',
    previewVueLoadFailed: 'Vue compiler could not load:',
    previewStarting: 'Starting preview…',
    previewUnavailable: 'Live preview unavailable (it could not load):',
    previewUnavailableHint: 'Save & Compile still builds on the server.',
    previewSfcNoScript: 'The Vue file has no script block.',
    previewCannotResolve: 'Cannot resolve {path} from {importer}',
    previewErrorBuildTitle: 'The preview could not be built',
    previewErrorRunTitle: 'The widget stopped with an error',
    previewErrorDetails: 'Details',
    previewErrorLine: '{file}:{line}',
    previewMissingFile: 'Missing file {path}',
    previewVueCompile: 'Vue compile ({path}): {message}',
    previewSdkHttp: 'The preview SDK could not be fetched (HTTP {status}).',
    previewWidgetName: 'Preview',
});

// `{name}` placeholders; an unknown id shows itself so a gap is visible, never blank.
function t(id, vars = {}) {
    const template = state.labels?.[id] ?? DEFAULT_LABELS[id] ?? id;
    return template.replace(/\{(\w+)\}/g, (match, key) => (key in vars ? String(vars[key]) : match));
}

// Sets every marked element. Run on open and again after the host's map arrives.
function applyLabels(labels) {
    state.labels = { ...DEFAULT_LABELS, ...labels };
    document.title = t('pageTitle');
    document.documentElement.lang = t('htmlLang');
    for (const el of document.querySelectorAll('[data-i18n]')) el.textContent = t(el.dataset.i18n);
    for (const el of document.querySelectorAll('[data-i18n-title]')) el.title = t(el.dataset.i18nTitle);
    for (const el of document.querySelectorAll('[data-i18n-aria]'))
        el.setAttribute('aria-label', t(el.dataset.i18nAria));
    for (const el of document.querySelectorAll('[data-i18n-placeholder]'))
        el.placeholder = t(el.dataset.i18nPlaceholder);
}

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
    model.updateOptions({ tabSize: EDITOR_TAB_SIZE, insertSpaces: true });
    model.onDidChangeContent(() => state.preview?.schedule());
    state.models.set(path, model);
    if (path.endsWith('.vue')) state.vueScripts?.attach(path, model);
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
    const name = normalizePath(window.prompt(t('newFilePrompt')) ?? '');
    if (!name || state.files.has(name)) return;
    flushActive();
    state.files.set(name, '');
    selectFile(name);
    state.preview?.schedule();
}

function renameFile(path) {
    if (path === state.entry) return;
    const next = normalizePath(window.prompt(t('renameFilePrompt'), path) ?? '');
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
    if (!window.confirm(t('deleteFileConfirm', { path }))) return;
    state.files.delete(path);
    disposeModel(path);
    if (state.active === path) state.active = state.entry;
    selectFile(state.active);
    state.preview?.schedule();
}

function disposeModel(path) {
    state.vueScripts?.detach(path);
    state.models.get(path)?.dispose();
    state.models.delete(path);
}

// Folder paths the operator has collapsed. Absent = expanded, so a new project opens fully visible.
const collapsedFolders = new Set();

// Group the flat path list into a nested tree so `lib/helper.ts` renders under a `lib` folder rather
// than as a row whose name happens to contain a slash.
function buildTree(paths) {
    const root = { folders: new Map(), files: [] };
    for (const path of paths) {
        const segments = path.split('/');
        const fileName = segments.pop();
        let node = root;
        let prefix = '';
        for (const segment of segments) {
            prefix = prefix ? `${prefix}/${segment}` : segment;
            if (!node.folders.has(segment)) {
                node.folders.set(segment, { name: segment, path: prefix, folders: new Map(), files: [] });
            }
            node = node.folders.get(segment);
        }
        node.files.push({ path, name: fileName });
    }
    return root;
}

function renderFiles() {
    const rows = [];
    const walk = (node, depth) => {
        for (const folder of [...node.folders.values()].sort((a, b) => a.name.localeCompare(b.name))) {
            const collapsed = collapsedFolders.has(folder.path);
            const item = document.createElement('li');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'tree-folder';
            button.style.setProperty('--depth', String(depth));
            button.setAttribute('aria-expanded', String(!collapsed));
            button.addEventListener('click', () => {
                if (collapsed) collapsedFolders.delete(folder.path);
                else collapsedFolders.add(folder.path);
                renderFiles();
            });

            const twisty = document.createElement('span');
            twisty.className = 'tree-twisty';
            twisty.textContent = '▾';
            const label = document.createElement('span');
            label.textContent = folder.name;
            button.append(twisty, label);
            item.append(button);
            rows.push(item);

            if (!collapsed) walk(folder, depth + 1);
        }

        for (const file of node.files.sort((a, b) => a.name.localeCompare(b.name))) {
            rows.push(fileRow(file.path, file.name, depth));
        }
    };
    walk(buildTree([...state.files.keys()]), 0);
    dom.fileList.replaceChildren(...rows);
}

function fileRow(path, name, depth) {
    const row = document.createElement('button');
    row.type = 'button';
    row.className = 'file-row tree-row';
    row.style.setProperty('--depth', String(depth));
    row.setAttribute('aria-current', String(path === state.active));
    row.title = path;
    row.addEventListener('click', () => selectFile(path));

    const label = document.createElement('span');
    label.className = 'file-name';
    label.textContent = name;
    row.append(label);

    if (path === state.entry) {
        const marker = document.createElement('span');
        marker.className = 'file-entry';
        marker.textContent = '•';
        marker.title = t('entryFile');
        row.append(marker);
    } else {
        row.append(
            fileAction(t('rename'), (event) => {
                event.stopPropagation();
                renameFile(path);
            }),
            fileAction(t('delete'), (event) => {
                event.stopPropagation();
                deleteFile(path);
            }),
        );
    }

    const item = document.createElement('li');
    item.append(row);
    return item;
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

// Strict, so the editor catches what the sandbox or the browser would only throw on stream. A script runs in a
// sandbox with no DOM, so its context gets no DOM types; a widget is a web page and gets them.
function configureLanguageServices(monaco, sdkTypes, context, framework) {
    const ts = monaco.languages.typescript;
    if (!ts) return;

    const compilerOptions = {
        target: ts.ScriptTarget.ESNext,
        module: ts.ModuleKind.ESNext,
        moduleResolution: ts.ModuleResolutionKind.NodeJs,
        jsx: framework === 'react' ? ts.JsxEmit.ReactJSX : ts.JsxEmit.Preserve,
        ...(framework === 'react' ? { jsxImportSource: 'react' } : {}),
        allowJs: true,
        checkJs: true,
        allowNonTsExtensions: true,
        noEmit: true,
        skipLibCheck: true,
        strict: true,
        noImplicitReturns: true,
        noFallthroughCasesInSwitch: true,
        noUncheckedIndexedAccess: true,
        // The server builds with esbuild, one file at a time: flag what a per-file transpile cannot do.
        isolatedModules: true,
        esModuleInterop: true,
        resolveJsonModule: true,
        lib: context === 'script' ? ['esnext'] : ['esnext', 'dom'],
    };

    // A file is highlighted as javascript or typescript purely by extension; the SDK must behave the same
    // either way, so both services get identical treatment.
    for (const service of [ts.javascriptDefaults, ts.typescriptDefaults]) {
        service.setCompilerOptions(compilerOptions);
        service.setDiagnosticsOptions({ noSemanticValidation: false, noSyntaxValidation: false });
        service.setEagerModelSync(true);
        if (sdkTypes) service.addExtraLib(sdkTypes, 'file:///nnz-sdk.d.ts');
        const frameworkLib = window.NNZ_EDITOR_LIBS?.[framework];
        if (frameworkLib) service.addExtraLib(frameworkLib, `file:///nnz-${framework}.d.ts`);
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
        tabSize: EDITOR_TAB_SIZE,
        rulers: [100],
        padding: { top: 10, bottom: 10 },
    });
}

// ── Problems + status ──────────────────────────────────────────────────────

const SEVERITY_LABEL = Object.freeze({ error: 'severityError', warning: 'severityWarning', info: 'severityInfo' });
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
    const markers = monaco.editor.getModelMarkers({}).filter((m) => !isHiddenScriptResource(m.resource));
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
            severity.textContent = t(SEVERITY_LABEL[severity.dataset.severity]);

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
            ? t('noProblems')
            : t('problemSummary', {
                  errors: t(errors === 1 ? 'errorsOne' : 'errorsMany', { count: errors }),
                  warnings: t(warnings === 1 ? 'warningsOne' : 'warningsMany', { count: warnings }),
              });
    dom.problemCount.dataset.severity = errors > 0 ? 'error' : warnings > 0 ? 'warning' : 'none';
    if (total === 0) dom.problems.hidden = true;

    dom.activityProblemBadge.hidden = errors === 0;
    dom.activityProblemBadge.textContent = String(errors);
    if (!document.querySelector('.view[data-view="problems"]')?.hidden) renderProblemsSidebar();
}

function syncStatus() {
    const position = state.editor.getPosition();
    if (position) dom.cursor.textContent = t('cursorPosition', { line: position.lineNumber, column: position.column });
    dom.language.textContent = state.editor.getModel()?.getLanguageId() ?? '';
}

// ── Preview ────────────────────────────────────────────────────────────────

function setPreviewCollapsed(collapsed) {
    dom.shell.dataset.preview = collapsed ? 'collapsed' : 'open';
    dom.togglePreview.textContent = t(collapsed ? 'showPreview' : 'hidePreview');
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

// ── Activity bar / side bar views ──────────────────────────────────────────

function showView(name) {
    for (const button of document.querySelectorAll('.activity-item')) {
        button.setAttribute('aria-current', String(button.dataset.view === name));
    }
    for (const section of document.querySelectorAll('.view')) {
        section.hidden = section.dataset.view !== name;
    }
    if (name === 'search') dom.searchInput.focus();
    if (name === 'problems') renderProblemsSidebar();
}

function renderProblemsSidebar() {
    if (!state.monaco) return;
    const markers = state.monaco.editor
        .getModelMarkers({})
        .filter((m) => !isHiddenScriptResource(m.resource));
    if (markers.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'view-hint';
        empty.textContent = t('noProblemsDetected');
        dom.problemsSidebar.replaceChildren(empty);
        return;
    }
    dom.problemsSidebar.replaceChildren(
        ...markers.map((marker) => {
            const file = String(marker.resource?.path ?? '').replace(/^\/+/, '');
            const item = document.createElement('li');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'problem-item';
            button.addEventListener('click', () => revealMarker(file, marker));

            const where = document.createElement('span');
            where.className = 'hit-where';
            where.textContent = `${file}:${marker.startLineNumber}`;
            const message = document.createElement('span');
            message.className = 'hit-line';
            message.textContent = marker.message;
            button.append(where, message);
            item.append(button);
            return item;
        }),
    );
}

function revealMarker(file, marker) {
    if (file && file !== state.active && state.files.has(file)) selectFile(file);
    state.editor.revealLineInCenter(marker.startLineNumber);
    state.editor.setPosition({ lineNumber: marker.startLineNumber, column: marker.startColumn });
    state.editor.focus();
}

// Plain substring search across the in-memory buffers — the project is a handful of files, so there is
// nothing to index and no worker to justify.
function runSearch(term) {
    const needle = term.trim().toLowerCase();
    if (needle.length < 2) {
        dom.searchResults.replaceChildren();
        return;
    }
    flushActive();
    const hits = [];
    for (const [path, content] of state.files) {
        content.split('\n').forEach((line, index) => {
            if (line.toLowerCase().includes(needle)) hits.push({ path, line: index + 1, text: line.trim() });
        });
    }
    if (hits.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'view-hint';
        empty.textContent = t('noMatchesFor', { term });
        dom.searchResults.replaceChildren(empty);
        return;
    }
    dom.searchResults.replaceChildren(
        ...hits.slice(0, 200).map((hit) => {
            const item = document.createElement('li');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'search-hit';
            button.addEventListener('click', () => {
                if (hit.path !== state.active) selectFile(hit.path);
                state.editor.revealLineInCenter(hit.line);
                state.editor.setPosition({ lineNumber: hit.line, column: 1 });
                state.editor.focus();
            });
            const where = document.createElement('span');
            where.className = 'hit-where';
            where.textContent = `${hit.path}:${hit.line}`;
            const text = document.createElement('span');
            text.className = 'hit-line';
            text.textContent = hit.text;
            button.append(where, text);
            item.append(button);
            return item;
        }),
    );
}

// ── Themes ─────────────────────────────────────────────────────────────────

const THEMES = Object.freeze([
    { id: 'vs-dark', labelId: 'themeDark' },
    { id: 'vs', labelId: 'themeLight' },
    { id: 'hc-black', labelId: 'themeHighContrastDark' },
    { id: 'hc-light', labelId: 'themeHighContrastLight' },
    { id: 'nnz-atom-one-dark', labelId: 'themeAtomOneDark' },
    { id: 'nnz-github-light', labelId: 'themeGitHubLight' },
    { id: 'nnz-dracula', labelId: 'themeDracula' },
    { id: 'nnz-monokai', labelId: 'themeMonokai' },
]);

const THEME_STORAGE_KEY = 'nnz.editor.theme';

// Hand-authored monaco.editor.IStandaloneThemeData for well-known editor themes Monaco does not ship
// built-in. Palettes are the real, publicly documented colors for each theme (not approximations).
function defineCustomThemes(monaco) {
    // Atom One Dark — https://github.com/atom/atom/tree/master/packages/one-dark-syntax (canonical palette).
    monaco.editor.defineTheme('nnz-atom-one-dark', {
        base: 'vs-dark',
        inherit: true,
        rules: [
            { token: 'comment', foreground: '5c6370', fontStyle: 'italic' },
            { token: 'string', foreground: '98c379' },
            { token: 'number', foreground: 'd19a66' },
            { token: 'keyword', foreground: 'c678dd' },
            { token: 'identifier', foreground: 'abb2bf' },
            { token: 'type', foreground: 'e5c07b' },
            { token: 'type.identifier', foreground: 'e5c07b' },
            { token: 'function', foreground: '61afef' },
            { token: 'variable', foreground: 'e06c75' },
            { token: 'delimiter', foreground: 'abb2bf' },
            { token: 'operator', foreground: '56b6c2' },
            { token: 'tag', foreground: 'e06c75' },
            { token: 'attribute.name', foreground: 'd19a66' },
        ],
        colors: {
            'editor.background': '#282c34',
            'editor.foreground': '#abb2bf',
            'editorLineNumber.foreground': '#495162',
            'editorLineNumber.activeForeground': '#abb2bf',
            'editor.selectionBackground': '#3e4451',
            'editor.inactiveSelectionBackground': '#3a3f4b',
            'editorCursor.foreground': '#528bff',
            'editor.lineHighlightBackground': '#2c313c',
            'editorIndentGuide.background': '#3b4048',
            'editorIndentGuide.activeBackground': '#4b5263',
            'editorWhitespace.foreground': '#3b4048',
            'editorGutter.background': '#282c34',
        },
    });

    // GitHub Light — https://github.com/primer/github-vscode-theme (light variant canonical palette).
    monaco.editor.defineTheme('nnz-github-light', {
        base: 'vs',
        inherit: true,
        rules: [
            { token: 'comment', foreground: '6e7781', fontStyle: 'italic' },
            { token: 'string', foreground: '0a3069' },
            { token: 'number', foreground: '0550ae' },
            { token: 'keyword', foreground: 'cf222e' },
            { token: 'identifier', foreground: '24292f' },
            { token: 'type', foreground: '953800' },
            { token: 'type.identifier', foreground: '953800' },
            { token: 'function', foreground: '8250df' },
            { token: 'variable', foreground: '24292f' },
            { token: 'delimiter', foreground: '24292f' },
            { token: 'operator', foreground: 'cf222e' },
            { token: 'tag', foreground: '116329' },
            { token: 'attribute.name', foreground: '0550ae' },
        ],
        colors: {
            'editor.background': '#ffffff',
            'editor.foreground': '#24292f',
            'editorLineNumber.foreground': '#8c959f',
            'editorLineNumber.activeForeground': '#24292f',
            'editor.selectionBackground': '#b6e3ff',
            'editor.inactiveSelectionBackground': '#e8f0fe',
            'editorCursor.foreground': '#0969da',
            'editor.lineHighlightBackground': '#f6f8fa',
            'editorIndentGuide.background': '#eaeef2',
            'editorIndentGuide.activeBackground': '#d0d7de',
            'editorWhitespace.foreground': '#d0d7de',
            'editorGutter.background': '#ffffff',
        },
    });

    // Dracula — https://draculatheme.com/contribute (official published palette).
    monaco.editor.defineTheme('nnz-dracula', {
        base: 'vs-dark',
        inherit: true,
        rules: [
            { token: 'comment', foreground: '6272a4', fontStyle: 'italic' },
            { token: 'string', foreground: 'f1fa8c' },
            { token: 'number', foreground: 'bd93f9' },
            { token: 'keyword', foreground: 'ff79c6' },
            { token: 'identifier', foreground: 'f8f8f2' },
            { token: 'type', foreground: '8be9fd' },
            { token: 'type.identifier', foreground: '8be9fd' },
            { token: 'function', foreground: '50fa7b' },
            { token: 'variable', foreground: 'f8f8f2' },
            { token: 'delimiter', foreground: 'f8f8f2' },
            { token: 'operator', foreground: 'ff79c6' },
            { token: 'tag', foreground: 'ff79c6' },
            { token: 'attribute.name', foreground: '50fa7b' },
        ],
        colors: {
            'editor.background': '#282a36',
            'editor.foreground': '#f8f8f2',
            'editorLineNumber.foreground': '#6272a4',
            'editorLineNumber.activeForeground': '#f8f8f2',
            'editor.selectionBackground': '#44475a',
            'editor.inactiveSelectionBackground': '#3a3d4d',
            'editorCursor.foreground': '#f8f8f0',
            'editor.lineHighlightBackground': '#2c2f3d',
            'editorIndentGuide.background': '#3b3e4d',
            'editorIndentGuide.activeBackground': '#565a6e',
            'editorWhitespace.foreground': '#3b3e4d',
            'editorGutter.background': '#282a36',
        },
    });

    // Monokai — canonical Sublime Text Monokai palette (widely documented, e.g. sublimetext.com default scheme).
    monaco.editor.defineTheme('nnz-monokai', {
        base: 'vs-dark',
        inherit: true,
        rules: [
            { token: 'comment', foreground: '75715e', fontStyle: 'italic' },
            { token: 'string', foreground: 'e6db74' },
            { token: 'number', foreground: 'ae81ff' },
            { token: 'keyword', foreground: 'f92672' },
            { token: 'identifier', foreground: 'f8f8f2' },
            { token: 'type', foreground: '66d9ef', fontStyle: 'italic' },
            { token: 'type.identifier', foreground: '66d9ef', fontStyle: 'italic' },
            { token: 'function', foreground: 'a6e22e' },
            { token: 'variable', foreground: 'f8f8f2' },
            { token: 'delimiter', foreground: 'f8f8f2' },
            { token: 'operator', foreground: 'f92672' },
            { token: 'tag', foreground: 'f92672' },
            { token: 'attribute.name', foreground: 'a6e22e' },
        ],
        colors: {
            'editor.background': '#272822',
            'editor.foreground': '#f8f8f2',
            'editorLineNumber.foreground': '#75715e',
            'editorLineNumber.activeForeground': '#f8f8f2',
            'editor.selectionBackground': '#49483e',
            'editor.inactiveSelectionBackground': '#3e3d32',
            'editorCursor.foreground': '#f8f8f0',
            'editor.lineHighlightBackground': '#3e3d32',
            'editorIndentGuide.background': '#3b3a32',
            'editorIndentGuide.activeBackground': '#54503f',
            'editorWhitespace.foreground': '#464741',
            'editorGutter.background': '#272822',
        },
    });
}

function applyTheme(id) {
    state.theme = id;
    state.monaco?.editor.setTheme(id);
    const known = THEMES.find((theme) => theme.id === id);
    dom.theme.textContent = t('themeLabel', { name: known ? t(known.labelId) : id });
    // Per-viewer convenience only; a browser that refuses storage must not break the editor.
    try {
        localStorage.setItem(THEME_STORAGE_KEY, id);
    } catch {
        /* private mode / storage disabled */
    }
}

function storedTheme() {
    try {
        return localStorage.getItem(THEME_STORAGE_KEY) ?? 'vs-dark';
    } catch {
        return 'vs-dark';
    }
}

// ── Command palette + quick open ───────────────────────────────────────────

const VIEW_LABEL = Object.freeze({
    explorer: 'explorer',
    search: 'search',
    problems: 'problems',
    run: 'runAndTest',
    history: 'history',
    bundle: 'bundle',
});

const palette = { items: [], filtered: [], index: 0, mode: 'files' };

function commands() {
    return [
        { label: t('saveAndCompile'), detail: 'Ctrl+S', run: requestSave },
        { label: t('runInSandbox'), detail: '', run: runSandbox },
        { label: t('formatDocument'), detail: '', run: () => state.editor?.getAction('editor.action.formatDocument')?.run() },
        { label: t('newFile'), detail: '', run: addFile },
        { label: t('togglePreview'), detail: '', run: () => setPreviewCollapsed(dom.shell.dataset.preview !== 'collapsed') },
        { label: t('toggleWordWrap'), detail: '', run: toggleWrap },
        { label: t('toggleMinimap'), detail: '', run: toggleMinimap },
        ...THEMES.map((theme) => ({ label: t('themeLabel', { name: t(theme.labelId) }), detail: theme.id, run: () => applyTheme(theme.id) })),
        ...[
            'explorer',
            'search',
            'problems',
            'run',
            // Only offered once the host actually wired an EditorHistory — matches the hidden activity item.
            ...(dom.historyActivityItem.hidden ? [] : ['history']),
            'bundle',
        ].map((view) => ({
            label: t('viewLabel', { name: t(VIEW_LABEL[view]) }),
            detail: '',
            run: () => showView(view),
        })),
    ];
}

function openPalette(mode) {
    palette.mode = mode;
    palette.items =
        mode === 'commands'
            ? commands()
            : [...state.files.keys()].sort().map((path) => ({ label: path, detail: '', run: () => selectFile(path) }));
    dom.paletteInput.value = mode === 'commands' ? '>' : '';
    dom.paletteBackdrop.hidden = false;
    filterPalette();
    dom.paletteInput.focus();
}

function closePalette() {
    dom.paletteBackdrop.hidden = true;
    state.editor?.focus();
}

function filterPalette() {
    const raw = dom.paletteInput.value;
    const commandMode = raw.startsWith('>');
    if (commandMode !== (palette.mode === 'commands')) {
        palette.mode = commandMode ? 'commands' : 'files';
        palette.items = commandMode
            ? commands()
            : [...state.files.keys()].sort().map((path) => ({ label: path, detail: '', run: () => selectFile(path) }));
    }
    const term = (commandMode ? raw.slice(1) : raw).trim().toLowerCase();
    palette.filtered = term
        ? palette.items.filter((item) => item.label.toLowerCase().includes(term))
        : palette.items;
    palette.index = 0;
    renderPalette();
}

function renderPalette() {
    if (palette.filtered.length === 0) {
        const empty = document.createElement('li');
        empty.className = 'palette-empty';
        empty.textContent = t('noMatches');
        dom.paletteList.replaceChildren(empty);
        return;
    }
    dom.paletteList.replaceChildren(
        ...palette.filtered.map((entry, index) => {
            const item = document.createElement('li');
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'palette-item';
            button.setAttribute('aria-selected', String(index === palette.index));
            button.addEventListener('click', () => {
                closePalette();
                entry.run();
            });
            const label = document.createElement('span');
            label.textContent = entry.label;
            button.append(label);
            if (entry.detail) {
                const detail = document.createElement('span');
                detail.className = 'palette-item-detail';
                detail.textContent = entry.detail;
                button.append(detail);
            }
            item.append(button);
            return item;
        }),
    );
}

function movePalette(delta) {
    if (palette.filtered.length === 0) return;
    palette.index = (palette.index + delta + palette.filtered.length) % palette.filtered.length;
    renderPalette();
    dom.paletteList.children[palette.index]?.scrollIntoView({ block: 'nearest' });
}

// ── Sandbox run ────────────────────────────────────────────────────────────

// Rebuilds the preview from the CURRENT buffers. Never posts to the host, so nothing is compiled
// server-side and no version is published — the whole point of testing before release.
function runSandbox() {
    showView('run');
    setPreviewCollapsed(false);
    state.preview?.rebuildNow();
}

function toggleWrap() {
    state.wrap = !state.wrap;
    state.editor?.updateOptions({ wordWrap: state.wrap ? 'on' : 'off' });
    if (state.wrap) state.editor?.setScrollLeft(0);
    dom.wrap.textContent = t(state.wrap ? 'wrapOn' : 'wrapOff');
}

function toggleMinimap() {
    state.minimap = !state.minimap;
    state.editor?.updateOptions({
        minimap: { enabled: state.minimap, renderCharacters: false, maxColumn: 80 },
    });
    dom.minimap.textContent = t(state.minimap ? 'minimapOn' : 'minimapOff');
}

// ── Save / close ───────────────────────────────────────────────────────────

function requestSave() {
    if (dom.save.disabled) return;
    flushActive();
    dom.save.disabled = true;
    dom.save.textContent = t('compiling');
    dom.result.hidden = true;
    state.pendingSave = new Map(state.files);
    postToHost({ type: HOST_MESSAGE.save, files: Object.fromEntries(state.files) });
}

const BUILD_MARKER_OWNER = 'nnz-build';

// The host's build errors become Error markers (owner 'nnz-build'), each ending at the end of its line.
// An error with no file goes on the entry file; one with no line, or naming a file the project lacks, is skipped.
function showBuildErrors(errors) {
    const monaco = state.monaco;
    if (!monaco) return;
    for (const model of monaco.editor.getModels()) monaco.editor.setModelMarkers(model, BUILD_MARKER_OWNER, []);

    const byFile = new Map();
    for (const error of Array.isArray(errors) ? errors : []) {
        const file = error?.file ?? state.entry;
        if (!state.files.has(file) || !Number.isInteger(error.line) || error.line < 1) continue;
        if (!byFile.has(file)) byFile.set(file, []);
        byFile.get(file).push(error);
    }

    for (const [file, fileErrors] of byFile) {
        const model = modelFor(file);
        const markers = fileErrors
            .filter((error) => error.line <= model.getLineCount())
            .map((error) => ({
                severity: monaco.MarkerSeverity.Error,
                message: error.message ?? '',
                code: error.code,
                startLineNumber: error.line,
                startColumn: Math.max(1, error.column ?? 1),
                endLineNumber: error.line,
                endColumn: Math.max(model.getLineMaxColumn(error.line), (error.column ?? 1) + 1),
            }));
        monaco.editor.setModelMarkers(model, BUILD_MARKER_OWNER, markers);
    }
}

function showCompileResult({ ok, message, errors }) {
    showBuildErrors(errors);
    // Only a save the host accepted makes its files the new baseline; a failed compile leaves them unsaved.
    if (ok && state.pendingSave) state.savedFiles = state.pendingSave;
    state.pendingSave = null;
    dom.result.hidden = false;
    dom.result.dataset.ok = String(Boolean(ok));
    dom.result.textContent = message ?? '';
    dom.save.disabled = false;
    dom.save.textContent = t('saveAndCompile');
}

function hasUnsavedEdits() {
    flushActive();
    if (state.files.size !== state.savedFiles.size) return true;
    for (const [path, content] of state.files) {
        if (state.savedFiles.get(path) !== content) return true;
    }
    return false;
}

// Close and Esc go through here: with unsaved edits the host hears nothing until the author chooses Discard.
function requestClose() {
    if (!hasUnsavedEdits()) {
        postToHost({ type: HOST_MESSAGE.close });
        return;
    }
    dom.unsavedBackdrop.hidden = false;
    dom.unsavedKeep.focus();
}

function keepEditing() {
    dom.unsavedBackdrop.hidden = true;
    state.editor?.focus();
}

function discardChanges() {
    dom.unsavedBackdrop.hidden = true;
    postToHost({ type: HOST_MESSAGE.close });
}

// ── History (S-CODE-COLLAPSE) ────────────────────────────────────────────

// Shows/hides the History activity item and renders its first page, or leaves it hidden entirely when the host
// opened this editor without an [EditorHistory] wired (e.g. a widget, which has its own separate version-history
// dialog outside the editor). `history` is `{ versions, hasMore }`, the same shape [renderHistoryPage] consumes.
function initHistory(history) {
    dom.historyActivityItem.hidden = !history;
    if (history) renderHistoryPage(history);
}

function requestHistoryLoadMore() {
    dom.historyLoadMore.disabled = true;
    dom.historyStatus.hidden = true;
    postToHost({ type: HOST_MESSAGE.historyLoadMore });
}

function requestHistoryRollback(versionId) {
    postToHost({ type: HOST_MESSAGE.historyRollback, versionId });
}

function requestHistoryDelete(versionId) {
    if (!window.confirm(t('deleteVersionConfirm'))) return;
    postToHost({ type: HOST_MESSAGE.historyDelete, versionId });
}

// Renders one full page — `{ versions, hasMore }` — replacing whatever was shown before (the host always sends
// the FULL list so far, matching [CodeScriptsController]'s own accumulate-on-load-more behavior).
function renderHistoryPage(page) {
    dom.historyLoadMore.disabled = false;
    dom.historyStatus.hidden = true;
    dom.historyLoadMore.hidden = !page.hasMore;
    dom.historyList.replaceChildren(
        ...(page.versions.length === 0
            ? [emptyHistoryRow()]
            : page.versions.map((version) => historyRow(version))),
    );
}

function emptyHistoryRow() {
    const item = document.createElement('li');
    item.className = 'view-hint';
    item.textContent = t('historyEmpty');
    return item;
}

function historyRow(version) {
    const item = document.createElement('li');
    item.className = 'history-row';

    const label = document.createElement('span');
    label.className = 'history-row-label';
    label.textContent = t('versionLabel', { version: version.version, status: version.validationStatus });
    if (version.isCurrent) {
        const current = document.createElement('span');
        current.className = 'history-row-current';
        current.textContent = ` ${t('current')}`;
        label.append(current);
    }
    item.append(label);

    // The currently-published version can't be rolled back onto itself or deleted (backend-enforced too).
    if (!version.isCurrent) {
        const publish = document.createElement('button');
        publish.type = 'button';
        publish.className = 'btn btn-quiet';
        publish.textContent = t('publish');
        publish.addEventListener('click', () => requestHistoryRollback(version.id));

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'btn btn-quiet';
        remove.textContent = t('delete');
        remove.addEventListener('click', () => requestHistoryDelete(version.id));

        item.append(publish, remove);
    }
    return item;
}

function showHistoryError(message) {
    dom.historyLoadMore.disabled = false;
    dom.historyStatus.hidden = false;
    dom.historyStatus.textContent = message || t('actionFailed');
}

// ── Test run (S-CODE-COLLAPSE) ───────────────────────────────────────────

// Shows/hides the Test run panel folded into Run & test. Hidden for anything without an [EditorTestRun] wired
// (widgets, which use the Run view's live iframe preview + fire bar instead — mutually exclusive with this).
function initTestRun(enabled, triggers, labels) {
    dom.testRun.hidden = !enabled;
    if (enabled) {
        fillTestRunPickers(triggers, labels);
        // The widget-only sandbox preview controls have nothing to drive for a code script (no DOM to render).
        dom.runTest.hidden = true;
        dom.runSandboxHint.hidden = true;
        dom.fireBar.hidden = true;
        dom.previewLog.hidden = true;
    }
}

function parseTestRunVariables(text) {
    const variables = {};
    for (const rawLine of text.split('\n')) {
        const line = rawLine.trim();
        if (!line || !line.includes('=')) continue;
        const key = line.slice(0, line.indexOf('=')).trim();
        const value = line.slice(line.indexOf('=') + 1).trim();
        if (key) variables[key] = value;
    }
    return variables;
}

// Trigger select: "Manual" (no trigger) then one option per server sample, labelled by the sample's label; role
// select: the role tokens the host named, default viewer. The words come from the host in `labels` - none live here.
// Picking a trigger fills the variables box with that sample's variables; the author's edits afterwards still win.
let testRunLabels = {};

function fillTestRunPickers(triggers, labels) {
    testRunLabels = labels;
    const samples = new Map(triggers.map((trigger) => [trigger.id, trigger]));
    dom.testRunTriggerLabel.textContent = labels.trigger ?? '';
    dom.testRunRoleLabel.textContent = labels.role ?? '';

    dom.testRunTrigger.replaceChildren(
        new Option(labels.manual ?? '', ''),
        ...triggers.map((trigger) => new Option(trigger.label, trigger.id)),
    );
    dom.testRunTrigger.onchange = () => {
        const sample = samples.get(dom.testRunTrigger.value);
        dom.testRunVars.value = sample
            ? Object.entries(sample.variables)
                  .map(([key, value]) => `${key}=${value}`)
                  .join('\n')
            : '';
    };
    // Nothing to pick between when the host sent no samples; the run stays "Manual".
    dom.testRunTrigger.hidden = triggers.length === 0;
    dom.testRunTriggerLabel.hidden = triggers.length === 0;

    const roles = Object.entries(labels.roles ?? {});
    dom.testRunRole.replaceChildren(...roles.map(([token, text]) => new Option(text, token)));
    dom.testRunRole.value = 'viewer';
    dom.testRunRole.hidden = roles.length === 0;
    dom.testRunRoleLabel.hidden = roles.length === 0;
}

function parseTestRunArgs(text) {
    return text.trim().length === 0 ? [] : text.trim().split(/\s+/);
}

function requestTestRun() {
    dom.testRunButton.disabled = true;
    dom.testRunButton.textContent = t('running');
    dom.testRunStatus.hidden = true;
    dom.testRunResult.hidden = true;
    postToHost({
        type: HOST_MESSAGE.testRun,
        variables: parseTestRunVariables(dom.testRunVars.value),
        args: parseTestRunArgs(dom.testRunArgs.value),
        trigger: dom.testRunTrigger.value || null,
        role: dom.testRunRole.value || null,
        files: Object.fromEntries(state.files),
    });
}

// Absent (not empty) when the run kind does not track them, so the panel never claims "none" for them.
function appendTestRunVariables(sections, data) {
    if (!data.variablesSet) {
        return;
    }
    const entries = Object.entries(data.variablesSet);
    sections.push(labelledSection(t('variablesSet'), entries.map(([key, value]) => `${key} = ${value}`)));
}

function labelledSection(label, lines) {
    return lines.length === 0 ? `${label}: ${t('none')}` : `${label}:\n${lines.join('\n')}`;
}

function showTestRunResult(data) {
    dom.testRunButton.disabled = false;
    dom.testRunButton.textContent = t('runTest');

    if (!data.ok) {
        dom.testRunStatus.hidden = false;
        dom.testRunStatus.dataset.ok = 'false';
        dom.testRunStatus.textContent = data.message || t('testRunFailed');
        dom.testRunResult.hidden = true;
        return;
    }

    // A run that threw underlines the line it threw on; a clean run clears the old underline.
    showBuildErrors(data.errors);
    dom.testRunStatus.hidden = false;
    dom.testRunStatus.dataset.ok = String(Boolean(data.success));
    dom.testRunStatus.textContent = data.success
        ? t('testRunSuccess', { ms: data.durationMs, calls: data.hostCallCount })
        : data.error
          ? t('testRunFailedWith', { error: data.error })
          : t('testRunFailedPlain');

    // One ordered list when the host sent a timeline; the older per-kind sections remain the fallback.
    const timeline = data.timeline ?? [];
    if (timeline.length > 0) {
        const tags = {
            chat: testRunLabels.timelineChat,
            effect: testRunLabels.timelineEffect,
            console: testRunLabels.timelineConsole,
        };
        const rows = timeline.map((row) => `${row.seq}  [${tags[row.kind] ?? row.kind}]  ${row.text}`);
        const sections = [`${testRunLabels.timeline ?? ''}:\n${rows.join('\n')}`];
        appendTestRunVariables(sections, data);
        dom.testRunResult.hidden = false;
        dom.testRunResult.textContent = sections.join('\n\n');
        return;
    }

    const sections = [
        labelledSection(t('chatOutput'), data.chatOutput),
        labelledSection(
            t('capturedEffects'),
            data.effects.map((effect) => `${effect.name}  ${effect.argsPreview}`),
        ),
    ];
    appendTestRunVariables(sections, data);
    if (data.console) sections.push(labelledSection(t('consoleOutput'), data.console));
    dom.testRunResult.hidden = false;
    dom.testRunResult.textContent = sections.join('\n\n');
}

// ── Boot ───────────────────────────────────────────────────────────────────

async function open(payload) {
    state.files = new Map(Object.entries(payload.files ?? {}));
    state.entry = payload.entry ?? [...state.files.keys()][0] ?? 'index.ts';
    if (!state.files.has(state.entry)) state.files.set(state.entry, '');
    state.active = state.entry;
    state.savedFiles = new Map(state.files);
    state.pendingSave = null;

    applyLabels(payload.labels ?? {});
    dom.title.textContent = payload.title ?? t('defaultTitle');
    dom.kind.textContent = payload.language ?? '';
    if (payload.accent) document.documentElement.style.setProperty('--accent', payload.accent);

    installSplitter();

    // Started before Monaco: the two toolchains load in parallel, and a Monaco failure still leaves a
    // working preview (and vice versa).
    state.preview = initPreview({
        frame: dom.previewFrame,
        note: dom.previewNote,
        errorBox: dom.previewError,
        onReveal: (file, line) => revealMarker(file, { startLineNumber: line, startColumn: 1 }),
        fireBar: dom.fireBar,
        log: dom.previewLog,
        refresh: dom.refresh,
        language: payload.language ?? '',
        entry: state.entry,
        fireSamples: payload.fireSamples ?? {},
        declaredEvents: payload.eventSubscriptions ?? [],
        widget: payload.widget ?? {},
        noteText: payload.previewNote ?? '',
        t,
        snapshotFiles,
    });

    // Nothing renders for a code script, so its preview pane would be dead width. The preview owns that
    // judgement — 'note' is exactly "this project has nothing to show".
    setPreviewCollapsed(state.preview.mode === 'note');

    dom.sdkTypesNotice.hidden = !payload.sdkTypesUnavailable;

    const monaco = await loadMonaco();
    state.monaco = monaco;
    configureLanguageServices(
        monaco,
        payload.sdkTypes ?? '',
        payload.language === 'script' ? 'script' : 'widget',
        payload.language,
    );
    state.vueScripts = createVueScriptModels(monaco);

    // Register every file up front, not lazily: the language service only sees files it has a model for, so
    // a helper the author has not clicked into would otherwise be invisible to cross-file resolution.
    for (const path of state.files.keys()) modelFor(path);

    state.editor = createEditor(monaco);
    state.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, requestSave);
    state.editor.onDidChangeCursorPosition(syncStatus);
    state.editor.onDidChangeModel(syncStatus);
    monaco.editor.onDidChangeMarkers((uris) => {
        for (const uri of uris) state.vueScripts.mirror(uri);
        renderProblems(monaco);
    });

    defineCustomThemes(monaco);
    applyTheme(storedTheme());
    renderFiles();
    renderTabs();
    syncStatus();
    renderProblems(monaco);
    renderBundleMeta(payload);
    dom.runNote.textContent =
        state.preview.mode === 'note'
            ? t('runNoteSandbox')
            : t('runNoteRender');

    initHistory(payload.history ?? null);
    initTestRun(Boolean(payload.testRunEnabled), payload.testTriggers ?? [], state.labels);

    dom.boot.hidden = true;
    dom.shell.hidden = false;
    state.editor.focus();
}

function renderBundleMeta(payload) {
    const rows = [
        [t('bundleName'), payload.title ?? '—'],
        [t('bundleKind'), payload.language || '—'],
        [t('bundleEntry'), state.entry],
        [t('bundleFiles'), String(state.files.size)],
    ];
    dom.bundleMeta.replaceChildren(
        ...rows.flatMap(([term, value]) => {
            const dt = document.createElement('dt');
            dt.textContent = term;
            const dd = document.createElement('dd');
            dd.textContent = value;
            return [dt, dd];
        }),
    );
}

function wireChrome() {
    dom.save.addEventListener('click', requestSave);
    dom.close.addEventListener('click', requestClose);
    dom.unsavedKeep.addEventListener('click', keepEditing);
    dom.unsavedDiscard.addEventListener('click', discardChanges);
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
    dom.wrap.addEventListener('click', toggleWrap);
    dom.minimap.addEventListener('click', toggleMinimap);
    dom.theme.addEventListener('click', () => openPalette('commands'));
    dom.runTest.addEventListener('click', runSandbox);
    dom.testRunButton.addEventListener('click', requestTestRun);
    dom.historyLoadMore.addEventListener('click', requestHistoryLoadMore);

    for (const button of dom.activity.querySelectorAll('.activity-item')) {
        button.addEventListener('click', () => showView(button.dataset.view));
    }
    dom.searchInput.addEventListener('input', () => runSearch(dom.searchInput.value));

    dom.paletteBackdrop.addEventListener('click', (event) => {
        if (event.target === dom.paletteBackdrop) closePalette();
    });
    dom.paletteInput.addEventListener('input', filterPalette);
    dom.paletteInput.addEventListener('keydown', (event) => {
        if (event.key === 'ArrowDown') {
            event.preventDefault();
            movePalette(1);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            movePalette(-1);
        } else if (event.key === 'Enter') {
            event.preventDefault();
            const entry = palette.filtered[palette.index];
            closePalette();
            entry?.run();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            closePalette();
        }
    });

    installSidebarSplitter();

    window.addEventListener('keydown', (event) => {
        const meta = event.ctrlKey || event.metaKey;
        if (event.key === 'Escape') {
            // Esc closes the palette first — closing the whole editor out from under an open palette
            // would lose unsaved work to a keystroke meant for the palette.
            if (!dom.paletteBackdrop.hidden) return;
            event.preventDefault();
            // Esc on the confirm is "Keep editing": the safe answer, never a second way to close.
            if (!dom.unsavedBackdrop.hidden) keepEditing();
            else requestClose();
        } else if (event.key === 'F1' || (meta && event.shiftKey && event.key.toLowerCase() === 'p')) {
            event.preventDefault();
            openPalette('commands');
        } else if (meta && !event.shiftKey && event.key.toLowerCase() === 'p') {
            event.preventDefault();
            openPalette('files');
        } else if (meta && event.shiftKey && event.key.toLowerCase() === 'e') {
            event.preventDefault();
            showView('explorer');
        } else if (meta && event.shiftKey && event.key.toLowerCase() === 'f') {
            event.preventDefault();
            showView('search');
        } else if (meta && event.shiftKey && event.key.toLowerCase() === 'm') {
            event.preventDefault();
            showView('problems');
        } else if (meta && event.shiftKey && event.key.toLowerCase() === 'd') {
            event.preventDefault();
            showView('run');
        } else if (meta && event.shiftKey && event.key.toLowerCase() === 'h' && !dom.historyActivityItem.hidden) {
            event.preventDefault();
            showView('history');
        }
    });
}

function installSidebarSplitter() {
    dom.sidebarSplitter.addEventListener('mousedown', (down) => {
        down.preventDefault();
        const onMove = (move) => {
            const left = dom.sidebar.getBoundingClientRect().left;
            dom.sidebar.style.flexBasis = `${Math.min(Math.max(move.clientX - left, 180), 480)}px`;
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

window.addEventListener('message', (event) => {
    if (event.origin !== window.location.origin) return;
    const data = event.data;
    if (data?.type === HOST_MESSAGE.open) {
        open(data.payload).catch((error) => {
            dom.boot.hidden = false;
            dom.boot.dataset.error = 'true';
            dom.bootMessage.textContent = t('editorStartFailed', { message: error.message });
        });
    } else if (data?.type === HOST_MESSAGE.requestClose) {
        // With the confirm already up, a second ask changes nothing: only Discard closes.
        if (dom.unsavedBackdrop.hidden) requestClose();
    } else if (data?.type === HOST_MESSAGE.compiled) {
        showCompileResult(data);
    } else if (data?.type === HOST_MESSAGE.historyPage) {
        renderHistoryPage(data.payload);
    } else if (data?.type === HOST_MESSAGE.historyError) {
        showHistoryError(data.message);
    } else if (data?.type === HOST_MESSAGE.testRunResult) {
        showTestRunResult(data);
    }
});

wireChrome();
postToHost({ type: HOST_MESSAGE.ready });
