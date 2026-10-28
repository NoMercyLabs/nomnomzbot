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

import type { ProxyOptions } from 'vite';

// Every first path segment the bot serves itself. The dev server (and the https tunnel that points at it)
// passes these to the bot; any other path is the dashboard's. A root missing here shows the dashboard page
// where the bot's page belongs: black overlays, a dead Stream Deck, an OBS bridge that never connects.
// botRoots.spec.ts reads the server's routes and fails when one is missing from this list.
export const botRoots: readonly string[] = [
  'api',
  'automation',
  'editor',
  'health',
  'hubs',
  'landing',
  'oauth-relay',
  'obs-bridge',
  'overlay',
  'voice-listener',
];

// Roots the server maps that the dev server must not pass on, each with its reason.
export const botRootsNotProxied: Readonly<Record<string, string>> = {
  'favicon.ico': 'the bot answers 404 on purpose; the dashboard ships its own icon',
};

// These carry a WebSocket (the SignalR hubs, the Automation API live stream).
const socketRoots: ReadonlySet<string> = new Set(['automation', 'hubs']);

// The proxy table: one entry per root. A root that needs more than the default gets it in `extra`.
export function botProxy(
  target: string,
  extra: Readonly<Record<string, ProxyOptions>> = {},
): Record<string, ProxyOptions> {
  return Object.fromEntries(
    botRoots.map((root: string): [string, ProxyOptions] => [
      `/${root}`,
      {
        target,
        xfwd: true,
        ...(socketRoots.has(root) ? { ws: true } : {}),
        ...extra[root],
      },
    ]),
  );
}
