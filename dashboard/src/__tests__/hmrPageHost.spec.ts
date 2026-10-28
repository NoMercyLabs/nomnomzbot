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

import { describe, expect, it } from 'vitest';

import { hmrPageHost } from '../../vite-plugins/hmrPageHost';

// Vite's client.mjs before its client-inject step, cut to the lines that name the live-reload socket hosts.
const servedClient: string = [
  'const importMetaUrl = new URL(import.meta.url);',
  'const socketHost = `${__HMR_HOSTNAME__ || importMetaUrl.hostname}:${hmrPort || importMetaUrl.port}${__HMR_BASE__}`;',
  'const directSocketHost = __HMR_DIRECT_TARGET__;',
].join('\n');

type Transform = (code: string, id: string) => { code: string } | null;

describe('hmrPageHost', () => {
  it('points the live-reload fallback at the page host, never at this machine', () => {
    const transform = hmrPageHost().transform as Transform;
    const result = transform(servedClient, '/node_modules/vite/dist/client/client.mjs');

    expect(result?.code).not.toContain('__HMR_DIRECT_TARGET__');
    expect(result?.code).toContain('const directSocketHost = `${importMetaUrl.host}/`;');
  });

  it('leaves every other module alone', () => {
    const transform = hmrPageHost().transform as Transform;

    expect(transform(servedClient, '/src/main.ts')).toBeNull();
  });
});
