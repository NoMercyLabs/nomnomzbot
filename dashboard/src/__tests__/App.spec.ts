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

import { dashboardHomeRoute } from '../views/dashboard/home/route'
import { loginRoute } from '../views/site/login/route'

type Router = typeof import('../router').default

const refreshOk = { data: { accessToken: 'token-from-cookie', expiresIn: 3600 } }

function stubFetch(refreshStatus: number): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      if (request.url.endsWith('/auth/refresh') && refreshStatus === 200)
        return new Response(JSON.stringify(refreshOk), { status: 200 })

      return new Response('{}', { status: refreshStatus })
    }),
  )
}

async function freshRouter(): Promise<Router> {
  vi.resetModules()
  const router = (await import('../router')).default

  return router
}

beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
  history.replaceState(null, '', '/')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('sign-in guard', () => {
  it('sends a signed-out visitor to the login page and remembers where they were going', async () => {
    stubFetch(401)
    const router = await freshRouter()

    await router.push('/app')

    expect(router.currentRoute.value.name).toBe(loginRoute.name)
    expect(router.currentRoute.value.query.return_to).toBe('/app')
    const { i18n } = await import('../i18n')

    expect(i18n.global.t('site.login.title')).toBe('Welcome')
  })

  it('lets a visitor with a refresh cookie into the dashboard', async () => {
    stubFetch(200)
    const router = await freshRouter()

    await router.push('/app')

    expect(router.currentRoute.value.name).toBe(dashboardHomeRoute.name)
  })

  it('takes the token from the OAuth fragment, strips it from the address and never stores it', async () => {
    stubFetch(401)
    history.replaceState(null, '', '/app#access_token=from-fragment&expires_in=3600')
    const router = await freshRouter()

    // The first navigation carries the whole address the browser opened, fragment included.
    await router.push(window.location.pathname + window.location.hash)

    const auth = await import('../api/auth')

    expect(router.currentRoute.value.name).toBe(dashboardHomeRoute.name)
    expect(auth.isSignedIn()).toBe(true)
    expect(window.location.hash).toBe('')
    const stored = JSON.stringify({ ...localStorage, ...sessionStorage })

    expect(stored).not.toContain('from-fragment')
  })

  // Stoney 2026-10-13: "the login screen should redirect to the app if the user has a valid session".
  it('sends a visitor with a valid session from the login page into the dashboard', async () => {
    stubFetch(200)
    const router = await freshRouter()

    await router.push({ name: loginRoute.name })

    expect(router.currentRoute.value.name).toBe(dashboardHomeRoute.name)
  })

  it('sends a signed-in visitor on to the page the login was asked for', async () => {
    stubFetch(200)
    const router = await freshRouter()

    await router.push({ name: loginRoute.name, query: { return_to: '/app/commands' } })

    expect(router.currentRoute.value.path).toBe('/app/commands')
  })

  it('never follows a return address to another site', async () => {
    stubFetch(200)
    const router = await freshRouter()

    await router.push({ name: loginRoute.name, query: { return_to: '//evil.example' } })

    expect(router.currentRoute.value.name).toBe(dashboardHomeRoute.name)
  })

  it('keeps a signed-out visitor on the login page', async () => {
    stubFetch(401)
    const router = await freshRouter()

    await router.push({ name: loginRoute.name })

    expect(router.currentRoute.value.name).toBe(loginRoute.name)
  })
})
