// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// Builds the SDK side view from the same declaration text the language service checks the code against, so
// the list can never name a call the runtime does not have. The server writes that text in one fixed shape
// (SdkRuntimeSurface): blocks open with `interface X {` or `declare const x: {`, members sit one per line
// with an optional /** doc */ above them, and a member typed with another block's name is a namespace.

const BLOCK_OPEN = /^(?:declare\s+const\s+(\w+)\s*:\s*\{|(?:export\s+)?interface\s+(\w+)(?:<[^>]*>)?(?:\s+extends\s+[^{]+)?\s*\{)$/;
const TYPED_GLOBAL = /^declare\s+const\s+(\w+)\s*:\s*(\w+)\s*;$/;
const METHOD = /^(\w+)\??\s*(?:<[^(]*>)?\(/;
const PROPERTY = /^(?:readonly\s+)?(\w+)\??\s*:\s*(.+?);?$/;

function braceDelta(line) {
    let delta = 0;
    for (const ch of line) {
        if (ch === '{') delta++;
        else if (ch === '}') delta--;
    }
    return delta;
}

function cleanDoc(lines) {
    return lines
        .map((line) => line.replace(/^\s*\/\*\*\s?/, '').replace(/\s*\*\/\s*$/, '').replace(/^\s*\*\s?/, ''))
        .join(' ')
        .replace(/\s+/g, ' ')
        .trim();
}

function parseMember(text) {
    const method = METHOD.exec(text);
    if (method) return { name: method[1], kind: 'method', signature: text.replace(/;$/, '') };
    const property = PROPERTY.exec(text);
    if (property) return { name: property[1], kind: 'property', type: property[2], signature: text.replace(/;$/, '') };
    return null;
}

// Reads every block and typed global out of the declaration text.
function parseDeclarations(dts) {
    const blocks = new Map();
    const globals = [];
    let current = null;
    let depth = 0;
    let doc = null;
    let pendingDoc = '';

    for (const raw of String(dts ?? '').split('\n')) {
        const line = raw.trim();
        if (doc !== null) {
            doc.push(line);
            if (line.endsWith('*/')) {
                pendingDoc = cleanDoc(doc);
                doc = null;
            }
            continue;
        }
        if (depth <= 1 && line.startsWith('/**')) {
            if (line.endsWith('*/')) pendingDoc = cleanDoc([line]);
            else doc = [line];
            continue;
        }

        if (depth === 0) {
            const open = BLOCK_OPEN.exec(line);
            if (open) {
                const global = open[1];
                const name = global ? `global:${global}` : open[2];
                current = { name, doc: pendingDoc, members: [] };
                blocks.set(name, current);
                if (global) globals.push({ name: global, type: name, doc: pendingDoc });
                depth = 1;
            } else {
                const typed = TYPED_GLOBAL.exec(line);
                if (typed) globals.push({ name: typed[1], type: typed[2], doc: pendingDoc });
            }
            if (line) pendingDoc = '';
            continue;
        }

        if (depth === 1 && current && line && line !== '}' && line !== '};') {
            const member = parseMember(line);
            if (member) current.members.push({ ...member, doc: pendingDoc });
        }
        if (line) pendingDoc = '';
        depth += braceDelta(line);
        if (depth <= 0) {
            depth = 0;
            current = null;
        }
    }
    return { blocks, globals };
}

function buildNode(blocks, path, name, typeName, doc, seen) {
    const block = blocks.get(typeName);
    const node = { name, path, kind: 'namespace', doc: doc || block?.doc || '', children: [] };
    if (!block || seen.has(typeName)) return node;
    const inner = new Set(seen).add(typeName);
    const byName = new Map();
    for (const member of block.members) {
        const memberPath = `${path}.${member.name}`;
        if (member.kind === 'property' && blocks.has(member.type)) {
            node.children.push(buildNode(blocks, memberPath, member.name, member.type, member.doc, inner));
            continue;
        }
        // Overloads share one entry; the first written doc describes the call.
        const existing = byName.get(member.name);
        if (existing) {
            existing.signatures.push(member.signature);
            if (!existing.doc) existing.doc = member.doc;
            continue;
        }
        const leaf = { name: member.name, path: memberPath, kind: member.kind, doc: member.doc, signatures: [member.signature] };
        byName.set(member.name, leaf);
        node.children.push(leaf);
    }
    return node;
}

const HELPER_MAP_OPEN = /^interface\s+NnzTemplateHelperMap\s*\{$/;
const HELPER_ROW = /^'((?:[^'\\]|\\.)+)':\s*'((?:[^'\\]|\\.)*)';$/;
const unescapeLiteral = (text) => text.replace(/\\(.)/g, '$1');

// The helper names live in one quoted-key block (NnzTemplateHelperMap): name to a one-line description.
function parseHelperRows(dts) {
    const rows = [];
    let inMap = false;
    for (const raw of String(dts ?? '').split('\n')) {
        const line = raw.trim();
        if (!inMap) {
            inMap = HELPER_MAP_OPEN.test(line);
            continue;
        }
        if (line === '}') break;
        const row = HELPER_ROW.exec(line);
        if (row) rows.push({ key: unescapeLiteral(row[1]), doc: unescapeLiteral(row[2]) });
    }
    return rows;
}

// The group of a helper is the text before its first dot; a name with no dot is a general helper.
function helperGroup(key) {
    const dot = key.indexOf('.');
    return dot > 0 ? key.slice(0, dot) : 'general';
}

const HELPERS_ROOT = 'template helpers';

/** The template helpers as one root: a branch per namespace, a leaf per helper that inserts `{name}`. */
function buildHelperRoot(rows) {
    const groups = new Map();
    for (const row of rows) {
        const group = helperGroup(row.key);
        if (!groups.has(group)) groups.set(group, []);
        groups.get(group).push(row);
    }
    const children = [...groups.keys()].sort().map((group) => ({
        name: group,
        path: `${HELPERS_ROOT}.${group}`,
        kind: 'namespace',
        doc: '',
        children: groups.get(group).map((row) => ({
            name: row.key,
            path: row.key,
            kind: 'helper',
            doc: row.doc,
            signatures: [`{${row.key}}`],
        })),
    }));
    return { name: HELPERS_ROOT, path: HELPERS_ROOT, kind: 'namespace', doc: 'Names to write inside a template, as {name} or {name:argument}.', children };
}

/**
 * The SDK as a tree: one root per global, namespaces nested, each call with its signatures and doc, then one
 * Template helpers root (grouped by namespace) when the declarations carry the helper map.
 */
export function buildSdkReference(dts) {
    const { blocks, globals } = parseDeclarations(dts);
    const roots = globals
        .filter((global) => blocks.has(global.type))
        .map((global) => buildNode(blocks, global.name, global.name, global.type, global.doc, new Set()));
    const helpers = parseHelperRows(dts);
    return helpers.length > 0 ? [...roots, buildHelperRoot(helpers)] : roots;
}

/** The calls whose path, signature or doc holds every word of the filter, with their namespaces kept. */
export function filterSdkReference(tree, filter) {
    const words = String(filter ?? '').toLowerCase().split(/\s+/).filter(Boolean);
    if (words.length === 0) return tree;
    const matches = (node) => {
        const text = `${node.path} ${(node.signatures ?? []).join(' ')} ${node.doc}`.toLowerCase();
        return words.every((word) => text.includes(word));
    };
    const walk = (node) => {
        if (node.kind !== 'namespace') return matches(node) ? node : null;
        const children = node.children.map(walk).filter(Boolean);
        return children.length > 0 ? { ...node, children } : null;
    };
    return tree.map(walk).filter(Boolean);
}

/** The text a click puts in the editor: a call gets its parentheses, a helper its braces, a value only its path. */
export function insertTextFor(node) {
    if (node.kind === 'helper') return `{${node.path}}`;
    return node.kind === 'method' ? `${node.path}()` : node.path;
}
