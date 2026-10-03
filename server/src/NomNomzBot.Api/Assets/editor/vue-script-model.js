// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Type checking for the <script> of a .vue file. The visible model stays html, so the author keeps template
// highlighting; the TypeScript worker cannot read a .vue file, so a hidden TypeScript model carries only the
// script text. Every other character of the file becomes a space and every line break stays, so a line and a
// column in the hidden model are the same line and column in the .vue file: no offset mapping anywhere.

const HIDDEN_SUFFIX = '.__script.ts';
const MARKER_OWNER = 'nnz-vue';
const SYNC_DELAY_MS = 250;
const SCRIPT_BLOCK = /(<script\b[^>]*>)([\s\S]*?)(<\/script\s*>)/gi;

const MODULE_MARKER_LINE = '\nexport {};';

// The template reads script bindings the hidden model cannot see, so "declared but never read" is not a fact here.
const UNUSED_CODES = new Set([6133, 6192, 6196, 6198, 6199, 6205]);
const isTemplateUse = (marker) => UNUSED_CODES.has(Number(marker.code?.value ?? marker.code));

const blankedOutsideLineBreaks = (text) => text.replace(/[^\r\n]/g, ' ');

export function scriptOnlyText(source) {
    let result = '';
    let cursor = 0;
    for (const block of source.matchAll(SCRIPT_BLOCK)) {
        const [, open, body] = block;
        result += blankedOutsideLineBreaks(source.slice(cursor, block.index + open.length));
        result += body;
        cursor = block.index + open.length + body.length;
    }
    // isolatedModules (TS1208) refuses a file with no import or export. The line is appended after the last
    // character, so no earlier line or column moves; mirror() drops any marker that lands on it.
    return `${result}${blankedOutsideLineBreaks(source.slice(cursor))}${MODULE_MARKER_LINE}`;
}

export function isHiddenScriptResource(resource) {
    return String(resource?.path ?? '').endsWith(HIDDEN_SUFFIX);
}

// One hidden model per .vue file, kept in step with it, with its diagnostics copied back onto the visible model.
export function createVueScriptModels(monaco) {
    const entries = new Map();

    const hiddenUriOf = (path) => monaco.Uri.parse(`file:///${path}${HIDDEN_SUFFIX}`);

    function attach(path, visible) {
        if (entries.has(path)) return;
        const uri = hiddenUriOf(path);
        const hidden =
            monaco.editor.getModel(uri) ??
            monaco.editor.createModel(scriptOnlyText(visible.getValue()), 'typescript', uri);

        const entry = { visible, hidden, timer: 0, subscription: null };
        entry.subscription = visible.onDidChangeContent(() => {
            clearTimeout(entry.timer);
            entry.timer = setTimeout(() => hidden.setValue(scriptOnlyText(visible.getValue())), SYNC_DELAY_MS);
        });
        entries.set(path, entry);
    }

    function detach(path) {
        const entry = entries.get(path);
        if (!entry) return;
        clearTimeout(entry.timer);
        entry.subscription.dispose();
        entry.hidden.dispose();
        entries.delete(path);
    }

    // Called with every resource whose markers changed: a hidden model's diagnostics land on its .vue file.
    function mirror(resource) {
        if (!isHiddenScriptResource(resource)) return;
        for (const entry of entries.values()) {
            if (entry.hidden.uri.toString() !== resource.toString() || entry.visible.isDisposed()) continue;
            const lastVisibleLine = entry.visible.getLineCount();
            const markers = monaco.editor
                .getModelMarkers({ resource: entry.hidden.uri })
                .filter((marker) => marker.startLineNumber <= lastVisibleLine && !isTemplateUse(marker))
                .map((marker) => ({
                severity: marker.severity,
                message: marker.message,
                code: marker.code,
                source: marker.source,
                startLineNumber: marker.startLineNumber,
                startColumn: marker.startColumn,
                endLineNumber: marker.endLineNumber,
                endColumn: marker.endColumn,
                tags: marker.tags,
            }));
            monaco.editor.setModelMarkers(entry.visible, MARKER_OWNER, markers);
        }
    }

    return { attach, detach, mirror };
}
