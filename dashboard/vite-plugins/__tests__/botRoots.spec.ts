// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve, sep } from 'node:path';

import { describe, expect, it } from 'vitest';

import { botProxy, botRoots, botRootsNotProxied } from '../botRoots';

// Vitest runs from the dashboard folder; the server sits beside it.
const serverSource: string = resolve(process.cwd(), '../server/src');
const viteConfig: string = readFileSync(resolve(process.cwd(), 'vite.config.ts'), 'utf8');

const ROUTE_ATTRIBUTE: RegExp = /\[Route\("\/?([^"/[{]+)[^"]*"\)\]/g;
const MAPPED_PATH: RegExp = /\.Map(?:Get|Post|Put|Delete|Hub<[\w.]+>|Fallback)?\(\s*"\/([^"/{]+)/g;
// A folder of static files served under a path: RequestPath = "/editor", or the constant that holds it.
const STATIC_PATH: RegExp = /(?:RequestPath|EditorPath)\s*=\s*"\/([^"/{]+)/g;

function sourceFiles(): string[] {
  return readdirSync(serverSource, { recursive: true, encoding: 'utf8' })
    .filter((file: string) => file.endsWith('.cs'))
    .filter((file: string) => !file.split(sep).some((part: string) => ['bin', 'obj', 'Migrations'].includes(part)))
    .map((file: string) => join(serverSource, file));
}

// The first path segment of every route the bot answers: [Route("...")] on a controller and Map...("/...").
function serverRoots(): Set<string> {
  const roots: Set<string> = new Set();

  for (const file of sourceFiles()) {
    const code: string = readFileSync(file, 'utf8');

    for (const pattern of [ROUTE_ATTRIBUTE, MAPPED_PATH, STATIC_PATH]) {
      for (const match of code.matchAll(pattern)) {
        roots.add(match[1]!);
      }
    }
  }

  return roots;
}

describe('the dev server passes every path the bot serves to the bot', () => {
  it('finds the routes it is meant to guard', () => {
    const roots: Set<string> = serverRoots();

    for (const known of ['api', 'hubs', 'overlay', 'automation', 'obs-bridge']) {
      expect(roots.has(known), `the scan should find /${known}`).toBe(true);
    }
  });

  it('lists every server root as proxied or as deliberately left out with a reason', () => {
    const covered: Set<string> = new Set([...botRoots, ...Object.keys(botRootsNotProxied)]);
    const missing: string[] = [...serverRoots()].filter((root: string) => !covered.has(root)).sort();

    expect(missing, 'add each to botRoots (vite-plugins/botRoots.ts) or to botRootsNotProxied with a reason').toEqual([]);
  });

  it('lists no root the server no longer serves', () => {
    const served: Set<string> = serverRoots();
    const stale: string[] = [...botRoots, ...Object.keys(botRootsNotProxied)].filter(
      (root: string) => !served.has(root),
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
