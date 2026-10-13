// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query'
import { createApp, ref, type App } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  adminTenantKey,
  adminTenantsKey,
  useAdminBeginAccess,
  useAdminImpersonate,
  useAdminReinstate,
  useAdminSuspend,
  useAdminTenant,
  useAdminTenants,
  type AdminTenantsParams,
} from '../admin'

type Answer = {
  data?: unknown
  error?: unknown
  response: { ok: boolean; status: number }
}
type Call = [
  path: string,
  init: {
    params?: { path?: Record<string, unknown>; query?: Record<string, unknown> }
    body?: unknown
  },
]

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}))

vi.mock('../client', () => ({ client: http }))

const ok = { ok: true, status: 200 } as const satisfies Answer['response']
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0))

const tenant = (id: string, status: string): Record<string, unknown> => ({
  id,
  name: id,
  twitchChannelId: `t-${id}`,
  status,
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-01-01T00:00:00Z',
  suspendedAt: null,
})

function mount<T>(
  use: () => T,
  queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
): { queries: QueryClient; hook: T } {
  let hook!: T
  const app: App = createApp({
    setup() {
      hook = use()

      return () => null
    },
  })

  app.use(VueQueryPlugin, { queryClient: queries })
  app.mount(document.createElement('div'))

  return { queries, hook }
}

beforeEach(() => {
  http.GET.mockReset()
  http.POST.mockReset()
})

describe('useAdminTenants', () => {
  it('sends search, status, isLive, Page and Take as given and caches the page under the params key', async () => {
    http.GET.mockResolvedValue({
      data: { data: [tenant('a', 'active')], hasMore: false, total: 1 },
      response: ok,
    })

    const params: AdminTenantsParams = {
      search: 'ste',
      status: 'active',
      isLive: true,
      page: 2,
      take: 50,
    }
    const { hook, queries } = mount(() => useAdminTenants(params))

    await settle()
    const call = http.GET.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/tenants')

    expect(call[1].params?.query).toEqual({
      search: 'ste',
      status: 'active',
      isLive: true,
      Page: 2,
      Take: 50,
    })

    expect(hook.data.value?.data?.map((t) => t.id)).toEqual(['a'])
    expect(queries.getQueryData(adminTenantsKey(params))).toEqual(hook.data.value)
  })

  it('leaves empty filters out of the query', async () => {
    http.GET.mockResolvedValue({ data: { data: [] }, response: ok })
    mount(() => useAdminTenants({ search: '', page: 1, take: 25 }))
    await settle()
    expect((http.GET.mock.calls[0] as Call)[1].params?.query).toEqual({ Page: 1, Take: 25 })
  })

  it('asks again when the page changes and keeps the old page while it loads', async () => {
    http.GET.mockResolvedValueOnce({ data: { data: [tenant('a', 'active')] }, response: ok })
    http.GET.mockResolvedValueOnce({ data: { data: [tenant('b', 'active')] }, response: ok })
    const params = ref<AdminTenantsParams>({ page: 1, take: 25 })
    const { hook } = mount(() => useAdminTenants(params))

    await settle()
    params.value = { page: 2, take: 25 }
    await Promise.resolve()
    expect(hook.data.value?.data?.[0]?.id).toBe('a')
    await settle()
    expect((http.GET.mock.calls[1] as Call)[1].params?.query?.Page).toBe(2)
    expect(hook.data.value?.data?.[0]?.id).toBe('b')
  })

  it('turns a refusal into an error state, not an empty list', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } })
    const { hook } = mount(() => useAdminTenants({ page: 1, take: 25 }))

    await settle()
    expect(hook.isError.value).toBe(true)
    expect(hook.error.value?.message).toBe('admin tenants 403')
    expect(hook.data.value).toBeUndefined()
  })
})

describe('useAdminTenant', () => {
  it('reads the detail by id and caches it under the detail key', async () => {
    http.GET.mockResolvedValue({ data: { data: { id: 'a', status: 'active' } }, response: ok })
    const { hook, queries } = mount(() => useAdminTenant('a'))

    await settle()
    const call = http.GET.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/tenants/{broadcasterId}')
    expect(call[1].params?.path).toEqual({ version: '1', broadcasterId: 'a' })
    expect(hook.data.value).toEqual({ id: 'a', status: 'active' })
    expect(queries.getQueryData(adminTenantKey('a'))).toEqual({ id: 'a', status: 'active' })
  })

  it('does not ask while no tenant is chosen', async () => {
    mount(() => useAdminTenant(null))
    await settle()
    expect(http.GET).not.toHaveBeenCalled()
  })
})

describe('useAdminSuspend', () => {
  it('posts newStatus and reason, then marks the detail and every list page stale', async () => {
    http.POST.mockResolvedValue({ data: { status: 'success' }, response: ok })
    const queries = new QueryClient()
    const page: AdminTenantsParams = { page: 1, take: 25 }

    queries.setQueryData(adminTenantKey('a'), { id: 'a' })
    queries.setQueryData(adminTenantsKey(page), { data: [] })
    queries.setQueryData(adminTenantKey('b'), { id: 'b' })
    const { hook } = mount(() => useAdminSuspend(), queries)

    await hook.mutateAsync({ broadcasterId: 'a', newStatus: 'Suspended', reason: 'abuse' })
    const call = http.POST.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/tenants/{broadcasterId}/suspend')
    expect(call[1].params?.path).toEqual({ version: '1', broadcasterId: 'a' })
    expect(call[1].body).toEqual({ newStatus: 'Suspended', reason: 'abuse' })
    expect(queries.getQueryState(adminTenantKey('a'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(adminTenantsKey(page))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(adminTenantKey('b'))?.isInvalidated).toBe(false)
  })

  it('on failure rejects and leaves the cache untouched', async () => {
    http.POST.mockResolvedValue({
      error: { status: 'error' },
      response: { ok: false, status: 409 },
    })

    const queries = new QueryClient()
    const page: AdminTenantsParams = { page: 1, take: 25 }

    queries.setQueryData(adminTenantKey('a'), { id: 'a' })
    queries.setQueryData(adminTenantsKey(page), { data: [] })
    const { hook } = mount(() => useAdminSuspend(), queries)

    await expect(
      hook.mutateAsync({ broadcasterId: 'a', newStatus: 'Suspended', reason: 'abuse' }),
    ).rejects.toThrow('suspend failed')

    expect(queries.getQueryState(adminTenantKey('a'))?.isInvalidated).toBe(false)
    expect(queries.getQueryState(adminTenantsKey(page))?.isInvalidated).toBe(false)
  })
})

describe('useAdminReinstate', () => {
  it('posts only the justification, then marks the detail and the lists stale', async () => {
    http.POST.mockResolvedValue({ data: { status: 'success' }, response: ok })
    const queries = new QueryClient()
    const page: AdminTenantsParams = { page: 1, take: 25 }

    queries.setQueryData(adminTenantKey('a'), { id: 'a' })
    queries.setQueryData(adminTenantsKey(page), { data: [] })
    const { hook } = mount(() => useAdminReinstate(), queries)

    await hook.mutateAsync({ broadcasterId: 'a', justification: 'appeal won' })
    const call = http.POST.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/tenants/{broadcasterId}/reinstate')
    expect(call[1].body).toEqual({ justification: 'appeal won' })
    expect(queries.getQueryState(adminTenantKey('a'))?.isInvalidated).toBe(true)
    expect(queries.getQueryState(adminTenantsKey(page))?.isInvalidated).toBe(true)
  })

  it('on failure rejects and leaves the cache untouched', async () => {
    http.POST.mockResolvedValue({
      error: { status: 'error' },
      response: { ok: false, status: 500 },
    })

    const queries = new QueryClient()

    queries.setQueryData(adminTenantKey('a'), { id: 'a' })
    const { hook } = mount(() => useAdminReinstate(), queries)

    await expect(hook.mutateAsync({ broadcasterId: 'a', justification: 'x' })).rejects.toThrow(
      'reinstate failed',
    )

    expect(queries.getQueryState(adminTenantKey('a'))?.isInvalidated).toBe(false)
  })
})

describe('useAdminBeginAccess', () => {
  it('posts justification, breakGlass and expiresAt and returns the grant', async () => {
    http.POST.mockResolvedValue({
      data: { data: { id: 'g1', targetBroadcasterId: 'a' } },
      response: ok,
    })

    const { hook } = mount(() => useAdminBeginAccess())
    const grant = await hook.mutateAsync({
      broadcasterId: 'a',
      justification: 'ticket 4',
      breakGlass: false,
      expiresAt: null,
    })
    const call = http.POST.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/tenants/{broadcasterId}/access')
    expect(call[1].body).toEqual({ justification: 'ticket 4', breakGlass: false, expiresAt: null })
    expect(grant).toEqual({ id: 'g1', targetBroadcasterId: 'a' })
  })
})

describe('useAdminImpersonate', () => {
  it('posts the grant id and justification for the user and returns the token', async () => {
    http.POST.mockResolvedValue({
      data: { data: { accessToken: 'tok', sessionId: 's1' } },
      response: ok,
    })

    const { hook } = mount(() => useAdminImpersonate())
    const token = await hook.mutateAsync({
      userId: 'u1',
      accessGrantId: 'g1',
      justification: 'repro',
    })
    const call = http.POST.mock.calls[0] as Call

    expect(call[0]).toBe('/api/v{version}/admin/users/{userId}/impersonate')
    expect(call[1].params?.path).toEqual({ version: '1', userId: 'u1' })
    expect(call[1].body).toEqual({ accessGrantId: 'g1', justification: 'repro' })
    expect(token).toMatchObject({ accessToken: 'tok', sessionId: 's1' })
  })

  it('rejects when the server refuses', async () => {
    http.POST.mockResolvedValue({
      error: { status: 'error' },
      response: { ok: false, status: 403 },
    })

    const { hook } = mount(() => useAdminImpersonate())

    await expect(
      hook.mutateAsync({ userId: 'u1', accessGrantId: 'g1', justification: 'repro' }),
    ).rejects.toThrow('impersonate failed')
  })
})
