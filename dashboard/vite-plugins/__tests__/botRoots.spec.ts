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
import { join, resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import { botProxy, botRoots, botRootsNotProxied } from '../botRoots';

// Vitest runs from the dashboard folder; the server sits beside it.
const serverSource: string = resolve(process.cwd(), '../server/src');
const viteConfig: string = readFileSync(resolve(process.cwd(), 'vite.config.ts'), 'utf8');

const ROUTE_ATTRIBUTE: RegExp = /\[Route\("\/?([^"/[{]+)[^"]*"\)\]/g;
const MAPPED_PATH: RegExp = /\.Map(?:Get|Post|Put|Delete|Hub<[\w.]+>|Fallback)?\(\s*"\/([^"/{]+)/g;
// A folder of static files served under a path: RequestPath = "/editor", or the constant that holds it.
const STATIC_PATH: RegExp = /(?:RequestPath|EditorPath)\s*=\s*"\/([^"/{]+)/g;

// Build output and migrations hold no route. A folder with one of these names is not entered at all: a listing of
// the whole tree reads tens of thousands of build files, and a server build running beside the gate makes that
// listing many times slower.
const SKIPPED_FOLDERS: string[] = ['bin', 'obj', 'Migrations'];

function sourceFiles(folder: string): string[] {
  return readdirSync(folder, { withFileTypes: true }).flatMap((entry) => {
    if (entry.isDirectory())
      return SKIPPED_FOLDERS.includes(entry.name)
        ? []
        : sourceFiles(join(folder, entry.name));

    return entry.name.endsWith('.cs')
      ? [join(folder, entry.name)]
      : [];
  });
}

// The first path segment of every route the bot answers: [Route("...")] on a controller and Map...("/...").
function findServerRoots(): Set<string> {
  const roots: Set<string> = new Set();

  for (const file of sourceFiles(serverSource)) {
    const code: string = readFileSync(file, 'utf8');

    for (const pattern of [ROUTE_ATTRIBUTE, MAPPED_PATH, STATIC_PATH]) {
      for (const match of code.matchAll(pattern)) {
        roots.add(match[1]!);
      }
    }
  }

  return roots;
}

// Read the server once, while the file loads, so no test pays for the scan of thousands of files (the three tests
// below each scanned it again) and no test timeout covers it.
const serverRoots: Set<string> = findServerRoots();

describe('the dev server passes every path the bot serves to the bot', () => {
  it('finds the routes it is meant to guard', () => {
    for (const known of ['api', 'hubs', 'overlay', 'automation', 'obs-bridge']) {
      expect(serverRoots.has(known), `the scan should find /${known}`).toBe(true);
    }
  });

  it('lists every server root as proxied or as deliberately left out with a reason', () => {
    const covered: Set<string> = new Set([...botRoots, ...Object.keys(botRootsNotProxied)]);
    const missing: string[] = [...serverRoots].filter((root: string) => !covered.has(root)).sort();

    expect(missing, 'add each to botRoots (vite-plugins/botRoots.ts) or to botRootsNotProxied with a reason').toEqual([]);
  });

  it('lists no root the server no longer serves', () => {
    const stale: string[] = [...botRoots, ...Object.keys(botRootsNotProxied)].filter(
      (root: string) => !serverRoots.has(root),
    );

    // The health check is mapped as /health/live, so its root segment is "health" and is found above.
    expect(stale).toEqual([]);
  });

  it('builds one proxy entry per root, with a WebSocket where the bot upgrades one', () => {
    const table = botProxy('http://bot.test');

    expect(Object.keys(table).sort()).toEqual(botRoots.map((root: string) => `/${root}`).sort());
    expect(table['/hubs']?.ws).toBe(true);
    expect(table['/automation']?.ws).toBe(true);
    expect(table['/overlay']?.ws).toBeUndefined();
    expect(table['/overlay']?.target).toBe('http://bot.test');
  });

  it('keeps the extra options of one root without losing its target', () => {
    const table = botProxy('http://bot.test', { api: { changeOrigin: true } });

    expect(table['/api']).toMatchObject({ target: 'http://bot.test', xfwd: true, changeOrigin: true });
  });

  it('makes the service worker leave every one of those roots to the network', () => {
    const live: string = /const isLivePath[\s\S]*?\.test\(/.exec(viteConfig)![0];

    for (const root of botRoots) {
      expect(live, `the service worker list should name /${root}`).toContain(root);
    }
  });

  it('builds the dev server proxy from the one list', () => {
    expect(viteConfig).toContain('botProxy(api');
  });
});
