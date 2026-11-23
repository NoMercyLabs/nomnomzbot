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

import { readdirSync, readFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

const src = join(__dirname, '..');
const openTag = '<PanelCard';
const closeTag = '</PanelCard>';
const formControl = /<(Input|Textarea|Select|Switch|Combobox|Checkbox|Slider|NumberRow|FieldRow|\w*Field|\w*Picker)\b/;
const padAttribute = /(^|\s):?pad(\s|=|$)/;
const noBodyPadding = /(^|\s)(bare|headless)(\s|=|$)/;
const footerOrActions = /<template\s+#(footer|actions)\b[\s\S]*?<\/template>/g;

// A RowGroup or SummaryRow (list rows) runs edge to edge by design: its rows carry their own padding
// inside the p-2 body, so a control in a row is not a loose field and the card keeps p-2.
const listRows = /<(RowGroup|SummaryRow)\b[\s\S]*?<\/\1>/g;

// Cards that hold a list by design in another shape than RowGroup. Each entry is a file and the reason.
const fullBleedRows = new Set<string>([
  // Its sections are divide-y lists of SpamSettingRow rows that carry their own padding.
  'components/spam/SpamSettingsForm.vue',
  // A filter rail beside a hover-row queue whose checkboxes sit in rows that carry their own px-3.
  'views/dashboard/channel-points/components/RedemptionsPanel.vue',
  // Rows with hover wash plus an add row aligned by its own px-2; PermissionsView.spec pins p-2 and px-2.
  'views/dashboard/settings/permissions/components/TeamRolesPanel.vue',
]);

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory()) {
      return entry.name === '__tests__'
        ? []
        : vueFiles(path);
    }

    return path.endsWith('.vue')
      ? [path]
      : [];
  });
}

function relativePath(path: string): string {
  return relative(src, path).replace(/\\/g, '/');
}

// The end of an opening tag: the first `>` that is outside quotes and braces (an arrow `=>` sits inside braces or quotes).
function tagEnd(text: string, from: number): number {
  let quote = '';
  let depth = 0;

  for (let i = from; i < text.length; i++) {
    const char = text[i];

    if (quote) {
      if (char === quote) {
        quote = '';
      }
    } else if (char === '"' || char === '\'') {
      quote = char;
    } else if (char === '{') {
      depth++;
    } else if (char === '}') {
      depth--;
    } else if (char === '>' && depth === 0) {
      return i;
    }
  }

  return -1;
}

type Card = { attributes: string; body: string };

// Every PanelCard of a file with its own body: a nested card's body does not count for the outer card.
function cards(text: string): Card[] {
  const found: Card[] = [];
  let at = text.indexOf(openTag);

  while (at !== -1) {
    const end = tagEnd(text, at);
    const attributes = text.slice(at + openTag.length, end);
    const selfClosed = text[end - 1] === '/';
    const next = text.indexOf(openTag, end);
    const close = text.indexOf(closeTag, end);
    const nested = next !== -1 && next < close;

    if (!selfClosed) {
      const stop = nested
        ? next
        : close;

      found.push({ attributes, body: text.slice(end + 1, stop) });
    }

    at = text.indexOf(openTag, end);
  }

  return found;
}

function needsPad(card: Card): boolean {
  const body = card.body.replace(footerOrActions, '').replace(listRows, '');

  return formControl.test(body) &&
    !noBodyPadding.test(card.attributes) &&
    !padAttribute.test(card.attributes);
}

// Stoney 2026-11-23: "the titles are crooked, this is a thing that happens on many views not just this one".
// The head has px-4; a body without `pad` has p-2, so its fields sit 8 px left of the title.
describe('a panel card with form controls aligns its body with its title', () => {
  const owned = vueFiles(src).filter((path) => {
    const name = relativePath(path);

    return !name.startsWith('components/ui/') && !fullBleedRows.has(name);
  });

  it('sets pad on every PanelCard whose body holds a form control', () => {
    const crooked = owned
      .filter((path) => cards(readFileSync(path, 'utf8')).some(needsPad))
      .map(relativePath);

    expect(crooked).toEqual([]);
  });

  it('reads the real PanelCard blocks of the source tree', () => {
    const total = owned.reduce((sum, path) => sum + cards(readFileSync(path, 'utf8')).length, 0);

    expect(total).toBeGreaterThan(200);
  });
});
