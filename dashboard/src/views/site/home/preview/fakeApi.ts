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

// The fake data layer of the home page previews. The previews mount the real dashboard pages; those
// pages read through the typed api client. While a preview is open, one client middleware answers
// every request from the sample table below, so nothing leaves the browser. A path the table does not
// know gets a plain 404, and the page shows the error state it would show for any missing data.
// This layer is the one place outside src/api that holds the client: it is a client middleware, not a reader.
// eslint-disable-next-line no-restricted-imports
import { client } from '@/api/client';

import { sampleReplies } from './sampleReplies';

// The plain values a JSON answer is made of.
export type Json =
  | string |
  number |
  boolean |
  null |
  readonly Json[] |
  { readonly [key: string]: Json };

export type FakeReply = {
  method: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  // A path with `*` standing for one segment: /api/v1/channels/*/pipelines.
  path: string;
  body: (url: URL) => Json;
};

// Paths the table did not know since the layer started; a spec reads it to prove a preview is complete.
export const unmatched: string[] = [];

function toPattern(path: string): RegExp {
  const escaped = path.replace(/[.+?^${}()|[\]\\]/g, String.raw`\$&`);

  return new RegExp(`^${escaped.replaceAll('*', '[^/]+')}$`);
}

function json(status: number, body: Json): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

// The first sample reply for a request, or null when none fits.
export function answer(method: string, url: URL): Response | null {
  const found = sampleReplies.find(
    (reply) => reply.method === method && toPattern(reply.path).test(url.pathname),
  );

  if (found)
    return json(200, found.body(url));

  unmatched.push(`${method} ${url.pathname}`);

  if (import.meta.env.DEV) {
    console.warn('[preview] not in the sample:', method, url.pathname + url.search);
  }

  return json(404, { title: 'Not part of the sample', status: 404 });
}

// Starts answering. The returned function stops it; calls are counted, so two open previews share one layer.
let users = 0;
const middleware = {
  onRequest({ request }: { request: Request }) {
    return answer(request.method, new URL(request.url)) ?? undefined;
  },
};

export function startFakeApi(): () => void {
  if (users === 0) {
    client.use(middleware);
  }

  users += 1;

  return () => {
    users -= 1;

    if (users === 0) {
      client.eject(middleware);
    }
  };
}
