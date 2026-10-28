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

import createClient from 'openapi-fetch';
import type { paths } from './schema';

import { activeBot } from './activeBot';
import { activeChannelId, channelHeader } from './activeChannel';
import { viewAs, viewAsHeader } from './viewAs';

import { servedOrigin } from '@/lib/botAddress';

export const client = createClient<paths>({ baseUrl: servedOrigin });

// Every call goes to the chosen bot. Registered first, so the sign-in middleware sees the final address.
client.use({
  async onRequest({ request }) {
    const url = new URL(request.url);

    if (url.origin === activeBot.value)
      return request;

    const target = new URL(url.pathname + url.search, activeBot.value);
    // The body is read out: a Request passed as the init of another drops its stream body.
    const body = request.body
      ? await request.blob()
      : undefined;

    return new Request(target, {
      method: request.method,
      headers: request.headers,
      body,
      credentials: request.credentials,
      signal: request.signal,
    });
  },
});

// Every call names the channel being managed, so a list with no channel in its path answers for that channel and
// not for the caller's own. A header a call set itself stays.
client.use({
  onRequest({ request }) {
    const channelId = activeChannelId.value;

    if (channelId === null || request.headers.has(channelHeader))
      return request;

    request.headers.set(channelHeader, channelId);

    return request;
  },
});

// While the owner previews a smaller role, every call says so and the bot answers as that role.
client.use({
  onRequest({ request }) {
    if (viewAs.value === '')
      return request;

    request.headers.set(viewAsHeader, viewAs.value);

    return request;
  },
});
