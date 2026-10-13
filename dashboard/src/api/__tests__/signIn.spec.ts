// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, useQuery } from '@tanstack/vue-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, type EffectScope } from 'vue'

type SignIn = typeof import('../signIn')
type Auth = typeof import('../auth')

const start = { deviceCode: 'dev-1', userCode: 'ABCD-EFGH', verificationUri: 'https://twitch.tv/activate', interval: 5, expiresIn: 1800 }

type Seen = { at: number; path: string; query: string; body: string }

// The bot answers each poll with the next status from the list.
function stubPolls(statuses: string[]): Seen[] {
  const seen: Seen[] = []
  const began: number = Date.now()
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const url: URL = new URL(request.url)
      seen.push({ at: Date.now() - began, path: url.pathname, query: url.search, body: await request.text() })
      const status: string = statuses.shift() ?? 'pending'
      const auth = status === 'authorized' ? { accessToken: 'device-token', refreshToken: '' } : null
      return new Response(JSON.stringify({ data: { status, auth } }), { status: 200 })
    }),
  )
  return seen
}

async function load(): Promise<[SignIn, Auth]> {
  vi.resetModules()
  return [await import('../signIn'), await import('../auth')]
}

type Watch = { status: () => string | undefined; leave: () => void }

// Runs the query the way the code panel does; leaving the scope is the panel unmounting.
function watchApproval(signIn: SignIn): Watch {
  const scope: EffectScope = effectScope()
  const approval = scope.run(() => useQuery(signIn.deviceApprovalQuery('twitch', start), new QueryClient()))!
  return { status: () => approval.data.value?.status, leave: () => scope.stop() }
}

beforeEach(() => {
  history.replaceState(null, '', '/login')
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('redirect sign-in', () => {
  it('sends Twitch to its own route and comes back to the page asked for', async () => {
    const [signIn] = await load()

    expect(signIn.signInUrl('twitch', '/app/commands')).toBe(
      `${window.location.origin}/api/v1/auth/twitch?client=web&return_to=%2Fapp%2Fcommands`,
    )
  })

  it('sends any other provider to the shared authorize route', async () => {
    const [signIn] = await load()

    expect(signIn.signInUrl('kick', '/app')).toBe(
      `${window.location.origin}/api/v1/auth/kick/authorize?client=web&return_to=%2Fapp`,
    )
  })
})

describe('device code sign-in', () => {
  it('polls at the interval, waits 5 s longer after slow_down, and signs in when approved', async () => {
    vi.useFakeTimers()
    const seen: Seen[] = stubPolls(['pending', 'slow_down', 'authorized'])
    const [signIn, auth] = await load()

    const approval: Watch = watchApproval(signIn)
    await vi.advanceTimersByTimeAsync(60_000)

    expect(seen.map((poll) => poll.at)).toEqual([5000, 10000, 20000])
    expect(seen[0]).toMatchObject({ path: '/api/v1/auth/twitch/device/poll', query: '?client=web', body: '{"deviceCode":"dev-1"}' })
    expect(approval.status()).toBe('authorized')
    expect(auth.isSignedIn()).toBe(true)
  })

  it.each(['expired', 'denied', 'error'])('stops on %s and stays signed out', async (status) => {
    vi.useFakeTimers()
    const seen: Seen[] = stubPolls(['pending', status, 'authorized'])
    const [signIn, auth] = await load()

    const approval: Watch = watchApproval(signIn)
    await vi.advanceTimersByTimeAsync(60_000)

    expect(seen).toHaveLength(2)
    expect(approval.status()).toBe(status)
    expect(auth.isSignedIn()).toBe(false)
  })

  it('stops polling when the visitor leaves the code panel', async () => {
    vi.useFakeTimers()
    const seen: Seen[] = stubPolls(['pending', 'pending', 'authorized'])
    const [signIn, auth] = await load()

    const approval: Watch = watchApproval(signIn)
    await vi.advanceTimersByTimeAsync(5000)
    approval.leave()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(seen).toHaveLength(1)
    expect(auth.isSignedIn()).toBe(false)
  })
})
