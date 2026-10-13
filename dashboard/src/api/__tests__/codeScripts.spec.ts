// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { beforeEach, describe, expect, it, vi } from 'vitest'

import { fetchCodeScripts } from '../codeScripts'

let answer: { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

vi.mock('../client', () => ({
  client: { GET: vi.fn<() => Promise<unknown>>(async () => answer) },
}))

beforeEach(() => {
  answer = { data: { data: [] }, response: { ok: true, status: 200 } }
})

describe('fetchCodeScripts', () => {
  it('returns the scripts the bot lists', async () => {
    answer = { data: { data: [{ id: 's1', name: 'Greeter' }] }, response: { ok: true, status: 200 } }

    expect(await fetchCodeScripts()).toEqual([{ id: 's1', name: 'Greeter' }])
  })

  it('fails with the bot reason when the bot refuses, instead of reading as an empty list', async () => {
    answer = {
      error: {
        status: 'error',
        message: 'Custom code is not enabled for this channel. Enable it on the Features page.',
        code: 'FEATURE_DISABLED',
        args: [],
      },
      response: { ok: false, status: 403 },
    }

    await expect(fetchCodeScripts()).rejects.toThrow('Custom code is not enabled for this channel. Enable it on the Features page.')
  })
})
