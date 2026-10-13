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

type Auth = typeof import('../auth')
type QueryClientModule = typeof import('../queryClient')

const user = { id: 'u1', displayName: 'Stoney_Eagle' }

// The server accepts only the fresh token; the old one has expired.
function stubServer(refreshStatus: number): string[] {
  const seen: string[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const bearer: string = request.headers.get('Authorization') ?? 'none'
      seen.push(`${new URL(request.url).pathname} ${bearer}`)
      if (request.url.endsWith('/auth/refresh'))
        return refreshStatus === 200
          ? new Response(JSON.stringify({ data: { accessToken: 'fresh', expiresIn: 3600 } }), { status: 200 })
          : new Response('{}', { status: refreshStatus })
      if (bearer !== 'Bearer fresh') return new Response('{}', { status: 401 })
      return new Response(JSON.stringify({ data: user }), { status: 200 })
    }),
  )
  return seen
}

async function signedInWithExpiredToken(): Promise<[Auth, QueryClientModule]> {
  vi.resetModules()
  history.replaceState(null, '', '/app#access_token=expired')
  const auth: Auth = await import('../auth')
  const queries: QueryClientModule = await import('../queryClient')
  await auth.restoreSession()
  return [auth, queries]
}

beforeEach(() => {
  history.replaceState(null, '', '/')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('expired access token', () => {
  it('refreshes once and repeats the call with the new token', async () => {
    const seen: string[] = stubServer(200)
    const [auth, { queryClient }] = await signedInWithExpiredToken()

    const result = await queryClient.fetchQuery(auth.currentUserQuery)

    expect(result).toEqual(user)
    expect(seen).toEqual([
      '/api/v1/auth/me Bearer expired',
      '/api/v1/auth/refresh none',
      '/api/v1/auth/me Bearer fresh',
    ])
    expect(auth.isSignedIn()).toBe(true)
  })

  it('signs the visitor out when the refresh cookie is gone too', async () => {
    const seen: string[] = stubServer(401)
    const [auth, { queryClient }] = await signedInWithExpiredToken()

    const result = await queryClient.fetchQuery(auth.currentUserQuery)

    expect(result).toBeNull()
    expect(seen).toHaveLength(2)
    expect(auth.isSignedIn()).toBe(false)
  })
})
