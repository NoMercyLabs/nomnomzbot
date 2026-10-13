// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import createClient from 'openapi-fetch'
import type { paths } from './schema'

import { activeBot } from './activeBot'

import { servedOrigin } from '@/lib/botAddress'

export const client = createClient<paths>({ baseUrl: servedOrigin })

// Every call goes to the chosen bot. Registered first, so the sign-in middleware sees the final address.
client.use({
  async onRequest({ request }) {
    const url = new URL(request.url)

    if (url.origin === activeBot.value)
      return request

    const target = new URL(url.pathname + url.search, activeBot.value)
    // The body is read out: a Request passed as the init of another drops its stream body.
    const body = request.body
      ? await request.blob()
      : undefined

    return new Request(target, {
      method: request.method,
      headers: request.headers,
      body,
      credentials: request.credentials,
      signal: request.signal,
    })
  },
})
