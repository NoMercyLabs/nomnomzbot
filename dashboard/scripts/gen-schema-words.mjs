// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The bot's schema labels (pipeline actions, widget settings, template helpers) are LocalizedText keys that
// the server lists in server/i18n/schema-i18n-keys.manifest.json. Their words live once, in the KMP app's
// strings.xml (key dots become underscores). This copies every key that has words into
// src/i18n/schema/<locale>.json, so the dashboard shows the same words without a second copy to edit. It also
// copies the KMP step titles, which the server does not name.
// Run: npm run gen:schema-words. A key without words in strings.xml is left out; the page falls back.
// With --check it writes nothing and exits 1 when a committed file differs from what it would write.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { XMLParser } from 'fast-xml-parser';

const root = fileURLToPath(new URL('../../', import.meta.url));
const resources = `${root}app/composeApp/src/commonMain/composeResources`;
const folders = { en: 'values', nl: 'values-nl' };
const checkOnly = process.argv.includes('--check');

const { keys } = JSON.parse(readFileSync(`${root}server/i18n/schema-i18n-keys.manifest.json`, 'utf8'));

// Compose escapes quotes and new lines with a backslash.
function unescapeCompose(text) {
  return text.replace(/\\(.)/g, (_match, char) =>
    char === 'n'
      ? '\n'
      : char);
}

// vue-i18n reads { } @ $ | as message syntax; a literal one is written as {'x'}.
function escapeVueI18n(text) {
  return text.replace(/[{}@$|]/g, (char) => `{'${char}'}`);
}

function readStrings(folder) {
  const parser = new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: '',
    textNodeName: 'text',
    isArray: (name) => name === 'string',
    processEntities: true,
    trimValues: false,
    parseTagValue: false,
  });
  const xml = parser.parse(readFileSync(`${resources}/${folder}/strings.xml`, 'utf8'));
  const words = new Map();

  for (const entry of xml.resources.string ?? []) {
    words.set(entry.name, String(entry.text ?? ''));
  }

  return words;
}

function place(tree, key, value) {
  const parts = key.split('.');
  let node = tree;

  for (const part of parts.slice(0, -1)) {
    if (typeof node[part] === 'string')
      throw new Error(`${key}: "${part}" is both a word and a group`);

    node[part] ??= {};
    node = node[part];
  }

  node[parts.at(-1)] = value;
}

// The step titles and field labels the KMP editor shows: pipelines_block_<action type> becomes
// pipelineBlock.<action type>, pipelines_field_<field> becomes pipelineField.<field>. A word with a Compose
// format argument (%1$s) is a sentence for another place, so it stays out.
const editorWords = {
  pipelines_block_: 'pipelineBlock',
  pipelines_field_: 'pipelineField',
};

function editorTitles(words) {
  return Object.entries(editorWords).flatMap(([prefix, group]) =>
    [...words.entries()]
      .filter(([name, text]) =>
        name.startsWith(prefix) && !text.includes('%'))
      .map(([name, text]) =>
        [`${group}.${name.slice(prefix.length)}`, text]));
}

for (const [locale, folder] of Object.entries(folders)) {
  const words = readStrings(folder);
  const tree = {};
  let count = 0;

  for (const key of [...keys].sort()) {
    const text = words.get(key.replaceAll('.', '_'));

    if (text === undefined) continue;

    place(tree, key, escapeVueI18n(unescapeCompose(text)));
    count += 1;
  }

  for (const [key, text] of editorTitles(words).sort()) {
    place(tree, key, escapeVueI18n(unescapeCompose(text)));
  }

  const target = `${root}dashboard/src/i18n/schema/${locale}.json`;
  const content = `${JSON.stringify(tree, null, 2)}\n`;

  if (!checkOnly) {
    writeFileSync(target, content);
  } else if (readFileSync(target, 'utf8') !== content) {
    console.error(`${locale}: src/i18n/schema/${locale}.json is stale; run npm run gen:schema-words`);
    process.exitCode = 1;
  }

  console.log(`${locale}: ${count} of ${keys.length} keys`);
}
