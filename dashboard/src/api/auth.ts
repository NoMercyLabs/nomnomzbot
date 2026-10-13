// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { queryOptions, useQuery, type UseQueryReturnType } from '@tanstack/vue-query'

import type { components } from './schema'
import { client } from './client'
import { queryClient } from './queryClient'

export type CurrentUser = components['schemas']['CurrentUserDto']

const version = { path: { version: '1' } }

// The access token lives in this module only. The refresh token is the server's httpOnly cookie.
let accessToken: string | null = null
let restoring: Promise<boolean> | null = null

// A copy of each request, kept until its response, so a 401 can be sent again with a new token.
const pending = new Map<string, Request>()

client.use({
  onRequest({ request, id }) {
    if (accessToken) request.headers.set('Authorization', `Bearer ${accessToken}`)
    pending.set(id, request.clone())
    return request
  },
  async onResponse({ request, response, id }) {
    const original: Request | undefined = pending.get(id)
    pending.delete(id)
    if (response.status !== 401 || !original || request.url.endsWith('/auth/refresh')) return response
    accessToken = null
    if (!(await restoreSession())) return response
    original.headers.set('Authorization', `Bearer ${accessToken}`)
    return fetch(original)
  },
  onError({ id }) {
    pending.delete(id)
  },
})

export function isSignedIn(): boolean {
  return accessToken !== null
}

export function startSignIn(returnTo: string = '/'): void {
  const query = new URLSearchParams({ client: 'web', return_to: returnTo })
  window.location.assign(`/api/v1/auth/twitch?${query}`)
}

// The OAuth callback returns the access token in the URL fragment; take it once and strip it.
function consumeFragment(): boolean {
  const fragment = new URLSearchParams(window.location.hash.slice(1))
  const token = fragment.get('access_token')
  if (!token) return false
  accessToken = token
  history.replaceState(history.state, '', window.location.pathname + window.location.search)
  return true
}

async function refresh(): Promise<boolean> {
  const result: { data?: unknown; response: Response } = await client.POST(
    '/api/v{version}/auth/refresh',
    { params: version, credentials: 'include' },
  )
  const { response } = result
  // The OpenAPI document declares no 200 body for refresh yet, so the envelope is checked here.
  const payload: unknown = (result.data as { data?: unknown } | undefined)?.data
  if (!response.ok || typeof payload !== 'object' || payload === null) return false
  const token: unknown = (payload as { accessToken?: unknown }).accessToken
  if (typeof token !== 'string') return false
  accessToken = token
  return true
}

export function restoreSession(): Promise<boolean> {
  if (accessToken) return Promise.resolve(true)
  if (consumeFragment()) return Promise.resolve(true)
  restoring ??= refresh().finally(() => {
    restoring = null
  })
  return restoring
}

async function fetchCurrentUser(): Promise<CurrentUser | null> {
  const { data } = await client.GET('/api/v{version}/auth/me', { params: version })
  return data?.data ?? null
}

export const currentUserQuery = queryOptions({ queryKey: ['auth', 'me'], queryFn: fetchCurrentUser })

export function useCurrentUser(): UseQueryReturnType<CurrentUser | null, Error> {
  return useQuery(currentUserQuery)
}

export async function signOut(): Promise<void> {
  await client.POST('/api/v{version}/auth/logout', { params: version, credentials: 'include' })
  accessToken = null
  queryClient.clear()
}
