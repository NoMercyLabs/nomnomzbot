// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// An event sample the author edited is saved with the widget as the project file `events/<type>.json`, so it
// comes back on every later open, on any device. The editor keeps these files out of its code buffers: they are
// split off when a project opens and folded back in when it is saved. A type without a file uses the stock sample.

const EVENT_FOLDER = 'events/';
const EVENT_EXTENSION = '.json';
const JSON_INDENT = 2;

export function eventPathOf(type) {
    return `${EVENT_FOLDER}${type}${EVENT_EXTENSION}`;
}

function typeOfPath(path) {
    if (!path.startsWith(EVENT_FOLDER) || !path.endsWith(EVENT_EXTENSION)) return null;
    const type = path.slice(EVENT_FOLDER.length, -EVENT_EXTENSION.length);
    return type === '' || type.includes('/') ? null : type;
}

function parseSample(text) {
    try {
        const value = JSON.parse(text);
        return value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null;
    } catch {
        return null;
    }
}

// The project's files, split into the code the editor shows and the saved samples by event type.
// A sample file that is not a JSON object is ignored: the stock sample stays in force for that type.
export function splitSampleFiles(files) {
    const code = {};
    const samples = {};
    for (const [path, content] of Object.entries(files ?? {})) {
        const type = typeOfPath(path);
        if (type === null) {
            code[path] = content;
            continue;
        }
        const sample = parseSample(content);
        if (sample !== null) samples[type] = sample;
    }
    return { code, samples };
}

// The save payload: the code files plus one `events/<type>.json` per edited sample.
export function joinSampleFiles(code, samples) {
    const files = { ...code };
    for (const [type, sample] of Object.entries(samples ?? {})) {
        files[eventPathOf(type)] = JSON.stringify(sample, null, JSON_INDENT);
    }
    return files;
}

// The edited sample of one type after the author changed its text: null removes it (the text is the stock sample, so
// the type goes back to the default), undefined means the text is not a JSON object and the edit cannot be saved.
export function editedSample(text, stock) {
    const sample = parseSample(text);
    if (sample === null) return undefined;
    return JSON.stringify(sample) === JSON.stringify(stock) ? null : sample;
}
