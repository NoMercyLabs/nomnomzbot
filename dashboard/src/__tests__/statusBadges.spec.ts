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

// A status is a StatusBadge: a dot plus words, with one of five tones. A view never picks a Badge variant from a
// state (:variant="state === ..."), because that draws a tinted pill and a second status look. A file below keeps
// a bound variant for one stated reason; the list only shrinks.
const stateBadge = /<Badge\b[^>]*\s:variant=/;

const laneC = 'admin form in the lane C area; it moves onto StatusBadge when lane C lands';

const allowlist = {
  'views/dashboard/admin/AdminFeatureFlagsView.vue': laneC,
  'views/dashboard/admin/AdminSystemView.vue': laneC,
} as const satisfies Record<string, string>;

const src = join(__dirname, '..');

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory() && entry.name !== '__tests__')
      return vueFiles(path);

    return entry.isFile() && entry.name.endsWith('.vue')
      ? [path]
      : [];
  });
}

function viewFiles(): string[] {
  return vueFiles(join(src, 'views')).map((path) =>
    relative(src, path).replaceAll('\\', '/'));
}

function text(path: string): string {
  return readFileSync(join(src, path), 'utf8');
}

const allowed = Object.keys(allowlist);

describe('status badges', () => {
  it('spots a Badge whose variant is chosen from a state', () => {
    expect(stateBadge.test('<Badge :variant="failed ? \'destructive\' : \'secondary\'">')).toBe(true);
    expect(stateBadge.test('<Badge variant="outline">')).toBe(false);
    expect(stateBadge.test('<StatusBadge :tone="tone(row.status)" :label="row.status" />')).toBe(false);
  });

  it('chooses no Badge variant from a state in a view, except the files that state a reason', () => {
    const offenders = viewFiles()
      .filter((path) => stateBadge.test(text(path)))
      .filter((path) => !allowed.includes(path));

    expect(offenders).toEqual([]);
  });

  it('keeps no clean file on the allowlist', () => {
    const stale = allowed.filter((path) => !stateBadge.test(text(path)));

    expect(stale).toEqual([]);
  });
});
