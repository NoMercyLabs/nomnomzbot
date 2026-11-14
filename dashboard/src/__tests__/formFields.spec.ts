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

import { oneOf } from '../lib/array';

// Aaoa's layout audit (P2, adopt the Field system consistently): a form field is a Field with a FieldLabel,
// a FieldInfo for its hint and a FieldError for its message. A raw <Label>, or a raw <label> that carries
// `for` or the old `grid gap-1` wrapper, builds a second field style. A file that still does goes on the list
// below until its owner moves it to Field, and a clean file must leave it. The list is empty: every form is a Field.
const src = join(__dirname, '..');
const rawFieldLabel = /<Label\b|<label\b[^>]*(?:grid gap-1|\s:?for=)/;
const stillRaw: readonly string[] = [];

function vueSources(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return entry.name === '__tests__' || entry.name === 'ui'
        ? []
        : vueSources(path);

    return path.endsWith('.vue')
      ? [relative(src, path).replace(/\\/g, '/')]
      : [];
  });
}

function holdsRawFieldLabel(path: string): boolean {
  const text: string = readFileSync(join(src, path), 'utf8');

  return rawFieldLabel.test(text);
}

describe('form fields', () => {
  it('spots a raw label and the old grid wrapper', () => {
    expect(rawFieldLabel.test('<Label for="a">Name</Label>')).toBe(true);
    expect(rawFieldLabel.test('<label class="grid gap-1.5 text-sm">Name</label>')).toBe(true);
    expect(rawFieldLabel.test('<label for="a" class="text-sm">Name</label>')).toBe(true);
    expect(rawFieldLabel.test('<FieldLabel for="a">Name</FieldLabel>')).toBe(false);
    expect(rawFieldLabel.test('<label\n  v-for="item in items"\n  class="contents"\n>')).toBe(false);
    expect(rawFieldLabel.test('<label class="flex items-center gap-2 text-sm">x</label>')).toBe(false);
  });

  it('has no raw label outside the list of files that still own one', () => {
    const offenders = vueSources(src)
      .filter((path) => holdsRawFieldLabel(path))
      .filter((path) => oneOf(stillRaw, path) === undefined);

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the list', () => {
    const stale = stillRaw.filter((path) => !holdsRawFieldLabel(path));

    expect(stale).toEqual([]);
  });
});
