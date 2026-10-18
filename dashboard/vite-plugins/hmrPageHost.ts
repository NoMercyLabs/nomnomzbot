// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
