// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The data of a test event, edited as a JSON tab (`events/<type>.json`) in the main editor. These tabs are
// scratch: they are never part of the widget's files, so they are never saved and never count as unsaved edits.
// The sample's own keys and value types become a JSON schema, so a wrong type or an unknown key is underlined.

const EVENT_FOLDER = 'events/';
const EVENT_EXTENSION = '.json';
const SCHEMA_URI_ROOT = 'nnz://event-schema/';
const JSON_INDENT = 2;

export function eventPathOf(type) {
    return `${EVENT_FOLDER}${type}${EVENT_EXTENSION}`;
}

function typeOfValue(value) {
    if (Array.isArray(value)) return 'array';
    if (value === null) return null;
    return typeof value;
}

function schemaOf(value) {
    const kind = typeOfValue(value);
    if (kind === null) return {};
    if (kind === 'array') return value.length > 0 ? { type: 'array', items: schemaOf(value[0]) } : { type: 'array' };
    if (kind !== 'object') return { type: kind };
    return {
        type: 'object',
        properties: Object.fromEntries(Object.entries(value).map(([key, inner]) => [key, schemaOf(inner)])),
    };
}

// Only the top level is closed: the event's own fields are known, but what sits inside an object field is not.
function topLevelSchemaOf(sample) {
    const schema = schemaOf(sample);
    return schema.type === 'object' ? { ...schema, additionalProperties: false } : schema;
}

export function createEventTabs(monaco) {
    const tabs = new Map();

    function registerSchemas() {
        monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
            validate: true,
            allowComments: false,
            schemas: [...tabs.values()].map((tab) => ({
                uri: `${SCHEMA_URI_ROOT}${tab.type}`,
                fileMatch: [tab.model.uri.toString()],
                schema: tab.schema,
            })),
        });
    }

    return {
        has: (path) => tabs.has(path),
        paths: () => [...tabs.keys()],
        typeOf: (path) => tabs.get(path)?.type ?? null,
        modelFor: (path) => tabs.get(path)?.model ?? null,

        // Opening an event that already has a tab keeps what the author typed there.
        open(type, sample) {
            const path = eventPathOf(type);
            if (tabs.has(path)) return path;

            const uri = monaco.Uri.parse(`file:///${path}`);
            const model =
                monaco.editor.getModel(uri) ??
                monaco.editor.createModel(JSON.stringify(sample, null, JSON_INDENT), 'json', uri);
            model.updateOptions({ tabSize: JSON_INDENT, insertSpaces: true });
            tabs.set(path, { type, model, schema: topLevelSchemaOf(sample) });
            registerSchemas();
            return path;
        },

        close(path) {
            const tab = tabs.get(path);
            if (!tab) return;
            tabs.delete(path);
            registerSchemas();
            tab.model.dispose();
        },

        // Fire is only possible while the text is JSON; the syntax error itself shows in the Problems panel.
        parse(path) {
            const model = tabs.get(path)?.model;
            if (!model) return { ok: false };
            try {
                return { ok: true, value: JSON.parse(model.getValue()) };
            } catch {
                return { ok: false };
            }
        },
    };
}
