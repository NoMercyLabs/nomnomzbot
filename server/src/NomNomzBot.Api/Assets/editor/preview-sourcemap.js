// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// A minimal source map reader: the preview maps a runtime stack position in the bundle back to the file and
// line the author wrote. Only what that needs — the v3 "mappings" string, one lookup.

const BASE64 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

// Segments are delta-encoded, so the lines must be walked in order. The generated column restarts on every
// line; the source index, source line and source column carry across lines.
export function createSourceMap(map) {
    if (!map || typeof map.mappings !== 'string') return null;

    const lines = [];
    let source = 0;
    let sourceLine = 0;
    let sourceColumn = 0;

    for (const text of map.mappings.split(';')) {
        let column = 0;
        const entries = [];
        for (const raw of text.split(',')) {
            if (raw === '') continue;
            const fields = decodeSegment(raw);
            column += fields[0];
            if (fields.length < 4) continue;
            source += fields[1];
            sourceLine += fields[2];
            sourceColumn += fields[3];
            entries.push({ column, source, line: sourceLine, sourceColumn });
        }
        lines.push(entries);
    }

    return {
        // line is 1-based and column 0-based, as an error stack reports them.
        lookup(line, column) {
            const entries = lines[line - 1];
            if (!entries?.length) return null;
            let best = entries[0];
            for (const entry of entries) {
                if (entry.column > column) break;
                best = entry;
            }
            return { source: map.sources?.[best.source] ?? '', line: best.line + 1, column: best.sourceColumn };
        },
    };
}

function decodeSegment(text) {
    const numbers = [];
    let value = 0;
    let shift = 0;
    for (const char of text) {
        const digit = BASE64.indexOf(char);
        value += (digit & 31) << shift;
        if (digit & 32) {
            shift += 5;
            continue;
        }
        numbers.push(value & 1 ? -(value >> 1) : value >> 1);
        value = 0;
        shift = 0;
    }
    return numbers;
}
