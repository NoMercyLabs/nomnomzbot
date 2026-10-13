// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const saas: string = 'https://nomnomz.bot'

function stubServer(): { url: string; body: string; bearer: string | null }[] {
  const seen: { url: string; body: string; bearer: string | null }[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      seen.push({
        url: request.url,
        body: await request.text(),
        bearer: request.headers.get('Authorization'),
      })

      return new Response(JSON.stringify({ data: { status: 'pending' } }), { status: 200 })
    }),
  )

  return seen
}

beforeEach(() => {
  vi.resetModules()
  localStorage.clear()
  history.replaceState(null, '', '/')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

// The client talks to the bot the visitor chose, wherever it runs (Stoney 2026-10-13, translated from Dutch:
// "I must be able to use the SaaS with my own local client").
describe('the chosen bot', () => {
  it('is the bot that serves this page until the visitor chooses another', async () => {
    const { activeBot } = await import('../activeBot')
    const { servedOrigin } = await import('@/lib/botAddress')

    expect(activeBot.value).toBe(servedOrigin)
  })

  it('receives every API call, with the body intact', async () => {
    const seen = stubServer()
    const { switchBot } = await import('../auth')
    const { startDeviceSignIn } = await import('../signIn')
    const { client } = await import('../client')

    switchBot(saas)
    await startDeviceSignIn('twitch')

    await client.POST('/api/v{version}/auth/{provider}/device/poll', {
      params: { path: { version: '1', provider: 'twitch' }, query: { client: 'web' } },
      body: { deviceCode: 'abc' },
    })

    expect(seen.map((call) => new URL(call.url).origin)).toEqual([saas, saas])
    expect(new URL(seen[1]!.url).pathname).toBe('/api/v1/auth/twitch/device/poll')
    expect(JSON.parse(seen[1]!.body)).toEqual({ deviceCode: 'abc' })
  })

  it('drops the sign-in and every cached answer of the bot it leaves', async () => {
    const seen = stubServer()
    const auth = await import('../auth')
    const { queryClient } = await import('../queryClient')
    const { client } = await import('../client')

    auth.acceptAccessToken('token-of-the-old-bot')
    queryClient.setQueryData(['auth', 'me'], { id: 'u1' })

    auth.switchBot(saas)
    await client.GET('/api/v{version}/auth/me', { params: { path: { version: '1' } } })

    expect(auth.isSignedIn()).toBe(false)
    expect(queryClient.getQueryData(['auth', 'me'])).toBeUndefined()
    expect(seen[0]!.bearer).toBeNull()
  })

  it('is still chosen after a reload', async () => {
    const { switchBot } = await import('../auth')

    switchBot(saas)

    vi.resetModules()
    const { activeBot } = await import('../activeBot')

    expect(activeBot.value).toBe(saas)
  })

  it('falls back to this page for a stored value that is not a bot address', async () => {
    localStorage.setItem('nnz.bot', 'javascript:alert(1)')
    const { activeBot } = await import('../activeBot')
    const { servedOrigin } = await import('@/lib/botAddress')

    expect(activeBot.value).toBe(servedOrigin)
  })

  it('is where a redirect sign-in starts', async () => {
    const { switchBot } = await import('../auth')
    const { signInUrl } = await import('../signIn')

    switchBot(saas)

    expect(signInUrl('twitch', '/app')).toBe(
      `${saas}/api/v1/auth/twitch?client=web&return_to=%2Fapp`,
    )
  })
})
