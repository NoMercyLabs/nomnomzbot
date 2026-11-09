// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { readFileSync } from 'node:fs';

import type { Plugin } from 'vite';

import { legalFacts, legalPaths } from '../src/views/site/legal/legalFacts';

type Section = { title: string; p?: string[]; list?: string[] };
type Document = { title: string; sections: Section[] };
type Doc = keyof typeof legalPaths;
type Words = {
  site: {
    legal: { law: string; effective: string; privacy: Document; terms: Document };
  };
};

const mountPoint = '<div id="app"></div>';
const shellTitle = '<title>NomNomzBot</title>';

function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}

function renderSection(section: Section, index: number, say: (text: string) => string): string {
  const paragraphs = (section.p ?? []).map((line) => `<p>${say(line)}</p>`);
  const items = (section.list ?? []).map((item) => `<li>${say(item)}</li>`);
  const list = items.length > 0
    ? [`<ul>${items.join('')}</ul>`]
    : [];

  return [`<section><h2>${index + 1}. ${say(section.title)}</h2>`, ...paragraphs, ...list, '</section>'].join('');
}

function renderDocument(doc: Doc, words: Words['site']['legal']): string {
  const facts = { ...legalFacts, law: words.law, effective: legalFacts.effective.slice(0, 10) };
  const say = (text: string): string =>
    escapeHtml(text.replace(/\{(\w+)\}/g, (_match, name: keyof typeof facts) => facts[name]));
  const { title, sections } = words[doc];
  const body = sections.map((section, index) => renderSection(section, index, say));

  return `<main><h1>${say(title)}</h1><p>${say(words.effective)}</p>${body.join('')}</main>`;
}

function pageFor(doc: Doc, shell: string, words: Words['site']['legal']): string {
  if (!shell.includes(mountPoint)) throw new Error(`the page has no mount point for the ${doc} text`);

  return shell
    .replace(shellTitle, `<title>${escapeHtml(words[doc].title)} - NomNomzBot</title>`)
    .replace(mountPoint, `<div id="app">${renderDocument(doc, words)}</div>`);
}

// One copy of the app page per legal document, with the whole English text written inside it. A policy checker
// reads the file without running the app; in a browser the app mounts over the text and shows the styled page.
export function legalPages(): Plugin {
  return {
    name: 'nnz:legal-pages',
    apply: 'build',
    generateBundle: {
      order: 'post',
      handler(_options, bundle): void {
        const page = bundle['index.html'];

        if (page?.type !== 'asset') throw new Error('index.html is missing from the bundle');

        const words = (JSON.parse(
          readFileSync('./src/views/site/legal/i18n/en.json', 'utf8'),
        ) as Words).site.legal;

        for (const doc of ['privacy', 'terms'] as const) {
          this.emitFile({
            type: 'asset',
            fileName: `${legalPaths[doc]}.html`,
            source: pageFor(doc, String(page.source), words),
          });
        }
      },
    },
  };
}
