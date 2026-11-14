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

// Aaoa's layout todo P2 (public pages): the sign-in, invite, setup and not-found pages show loading, error
// and empty through one shared presentation. Loading is PublicWait, a failure or a notice is InlineNotice
// (or PanelCard state), nothing is empty text of its own; a radius is a token, never a one-off pixel value;
// a long-running action shows the shared Spinner and is disabled while it runs.
const site = join(__dirname, '..');

function vueFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    const path = join(folder, entry.name);

    if (entry.isDirectory())
      return entry.name === '__tests__'
        ? []
        : vueFiles(path);

    return path.endsWith('.vue')
      ? [relative(site, path).replace(/\\/g, '/')]
      : [];
  });
}

const files = vueFiles(site);

function matches(path: string, pattern: RegExp): boolean {
  const text: string = readFileSync(join(site, path), 'utf8');

  return pattern.test(text);
}

function offenders(pattern: RegExp, except: readonly string[] = []): string[] {
  return files
    .filter((path) => !except.includes(path))
    .filter((path) => matches(path, pattern));
}

describe('public page presentation', () => {
  it('finds the public pages it guards', () => {
    expect(files).toEqual(expect.arrayContaining([
      'login/LoginView.vue',
      'invite/InvitePage.vue',
      'setup/SetupView.vue',
      'not-found/NotFoundView.vue',
    ]));
  });

  it('writes no status or alert line by hand, only through PublicWait or InlineNotice', () => {
    expect(offenders(/\brole="(?:alert|status)"/, ['components/PublicWait.vue'])).toEqual([]);
  });

  it('colors no text red by hand', () => {
    expect(offenders(/\btext-destructive\b/)).toEqual([]);
  });

  it('uses no one-off radius, only the shared radius tokens and component variants', () => {
    expect(offenders(/\brounded-\[/)).toEqual([]);
  });

  it('shows the shared Spinner on every button that is disabled while a mutation runs', () => {
    const missing = files.flatMap((path) => {
      const text = readFileSync(join(site, path), 'utf8');
      const pending = [...text.matchAll(/:disabled="[^"]*?(\w+)\.isPending\.value/g)].map((hit) => hit[1]!);

      return pending
        .filter((name) => !text.includes(`<Spinner v-if="${name}.isPending.value"`))
        .map((name) => `${path}: ${name}`);
    });

    expect(missing).toEqual([]);
  });
});
