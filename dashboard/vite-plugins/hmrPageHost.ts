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

import type { Plugin } from 'vite';

// Vite fills this token in later, in its own client-inject step, so the swap runs before that step.
const directSocketHost: string = 'const directSocketHost = __HMR_DIRECT_TARGET__;';
const pageHost: string = 'const directSocketHost = `${importMetaUrl.host}/`;';

// Vite's client tries a second, direct live-reload socket to ws://localhost:<port> when the first one
// fails. On a page opened through the https tunnel that address is the visitor's own machine, and Chrome
// asks "wants to access other apps and services on this device" (Local Network Access). The fallback
// points at the page's own host instead, so no visitor's browser is sent to its loopback.
export function hmrPageHost(): Plugin {
  return {
    name: 'nnz:hmr-page-host',
    enforce: 'pre',
    apply: 'serve',
    transform(code: string, id: string): { code: string; map: null } | null {
      if (!id.endsWith('vite/dist/client/client.mjs') || !code.includes(directSocketHost))
        return null;

      return { code: code.replace(directSocketHost, pageHost), map: null };
    },
  };
}
