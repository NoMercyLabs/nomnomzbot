// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest'

import { probeBot } from '../system'

const bot: string = 'http://192.168.1.20:5080'
const status = { onboardingComplete: true, checks: {} }

type Answer = 'json' | 'cors' | 'down' | 'silent'

// "cors": the bot answers, but the browser hides the answer because the bot does not allow this page.
function stubBot(
  answer: Answer,
): Array<{ url: string; mode: string; authorization: string | null }> {
  const seen: Array<{ url: string; mode: string; authorization: string | null }> = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: Request | string, init?: RequestInit) => {
      const request = typeof input === 'string'
        ? new Request(input, init)
        : input
      const mode: string = init?.mode ?? 'cors'

      seen.push({ url: request.url, mode, authorization: request.headers.get('Authorization') })

      if (answer === 'down')
        throw new TypeError('Failed to fetch')

      if (answer === 'silent')
        throw new DOMException('The operation timed out.', 'TimeoutError')

      if (answer === 'cors' && mode !== 'no-cors')
        throw new TypeError('Failed to fetch')

      if (mode === 'no-cors')
        return new Response(null, { status: 200 })

      return new Response(JSON.stringify({ data: status }), { status: 200 })
    }),
  )

  return seen
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('probeBot', () => {
  it('reads the status of a bot that allows this page, without sending a token', async () => {
    const seen = stubBot('json')

    expect(await probeBot(bot, 'http://localhost:5090')).toEqual({
      state: 'ready',
      onboardingComplete: true,
    })
    expect(seen).toEqual([
      { url: `${bot}/api/v1/system/status`, mode: 'cors', authorization: null },
    ])
  })

  it('reports a bot that answers but does not allow this page', async () => {
    stubBot('cors')

    expect(await probeBot(bot, 'http://localhost:5090')).toEqual({ state: 'reachable' })
  })

  it('reports a bot that does not answer', async () => {
    stubBot('down')

    expect(await probeBot(bot, 'http://localhost:5090')).toEqual({ state: 'unreachable' })
  })

  it('gives up after one call when nothing answers in time', async () => {
    const seen = stubBot('silent')

    expect(await probeBot(bot, 'http://localhost:5090')).toEqual({ state: 'unreachable' })
    expect(seen).toHaveLength(1)
  })

  it('does not call a plain http bot from an https page, because the browser blocks it', async () => {
    const seen = stubBot('json')

    expect(await probeBot(bot, 'https://bot.example.com')).toEqual({ state: 'mixedContent' })
    expect(seen).toEqual([])
  })
})
