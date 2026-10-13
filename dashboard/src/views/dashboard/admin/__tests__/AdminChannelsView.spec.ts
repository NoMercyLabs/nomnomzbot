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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { queryClient } from '@/api/queryClient'
import { i18n } from '@/i18n'
import { dashboardRoutes } from '@/router/dashboard'

import AdminChannelsView from '../AdminChannelsView.vue'
import en from '../i18n/en.json'
import { dashboardAdminChannelsRoute } from '../route'

type Options = { params?: { query?: Record<string, unknown> } }
type Call = { path: string; query: Record<string, unknown> }
type Answer = {
  data?: unknown
  error?: unknown
  response: { status: number; ok: boolean }
}

const calls: Call[] = []
let rowsOnPage: Record<string, unknown>[] = []
let total: number = 0
let refused: boolean = false

const tenant = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  twitchChannelId: `tw-${id}`,
  status: 'active',
  billingTierKey: 'free',
  isLive: false,
  createdAt: '2026-06-29T10:00:00Z',
  suspendedAt: null,
  ...over,
})

vi.mock('@/api/client', () =>
  ({
    client: {
      use: () => undefined,
      GET: async (path: string, options?: Options): Promise<Answer> => {
        calls.push({ path, query: { ...options?.params?.query } })

        if (refused)
          return { error: { message: 'no' }, response: { status: 403, ok: false } }

        return {
          data: { data: rowsOnPage, total, hasMore: total > 25, nextPage: 2 },
          response: { status: 200, ok: true },
        }
      },
    },
  }))

let mounted: VueWrapper | null = null
let pinia: Pinia
let router: Router

beforeEach(() => {
  localStorage.clear()
  calls.length = 0
  refused = false
  total = 2
  rowsOnPage = [
    tenant('t1', 'stoney_eagle', { isLive: true }),
    tenant('t2', 'quiet_one', { status: 'suspended', createdAt: '2026-05-01T10:00:00Z' }),
  ]
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  queryClient.clear()
  vi.restoreAllMocks()
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(): Promise<void> {
  const blank = { template: '<div />' }

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/admin/channels', name: 'dashboard-admin-channels', component: blank }],
  })
  await router.push('/app/admin/channels')
  mounted = mount(AdminChannelsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })
  await settle()
}

const text = (): string => document.body.textContent ?? ''
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-channel-row]'),
]
const lastQuery = (): Record<string, unknown> => calls[calls.length - 1]!.query
const byText = (selector: string, label: string): HTMLElement | undefined =>
  [...document.body.querySelectorAll<HTMLElement>(selector)].find(
    (node) => node.textContent?.trim() === label,
  )

describe('AdminChannelsView', () => {
  it('renders one row per channel with its name, status, live state and created date', async () => {
    await open()

    expect(rows()).toHaveLength(2)
    expect(rows()[0]!.textContent).toContain('stoney_eagle')
    expect(rows()[0]!.textContent).toContain('Active')
    expect(rows()[0]!.textContent).toContain('Live')
    expect(rows()[0]!.textContent).toContain('Jun 29, 2026')
    expect(rows()[1]!.textContent).toContain('quiet_one')
    expect(rows()[1]!.textContent).toContain('Suspended')
    expect(rows()[1]!.textContent).toContain('Offline')
    expect(rows()[1]!.textContent).toContain('May 1, 2026')
    expect(text()).toContain('2 channels')
  })

  it('refetches with the typed search after the pause and goes back to page 1', async () => {
    total = 60
    await open()
    byText('button', 'Next')!.click()
    await settle()
    expect(lastQuery().Page).toBe(2)

    const input = document.body.querySelector('input[type="search"]') as HTMLInputElement

    input.value = 'stoney'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await settle()
    const before = calls.length

    expect(calls.some((call) => call.query.search === 'stoney')).toBe(false)

    await new Promise((done) => setTimeout(done, 450))
    await settle()
    expect(calls.length).toBeGreaterThan(before)
    expect(lastQuery().search).toBe('stoney')
    expect(lastQuery().Page).toBe(1)
  })

  it('sends the chosen status as the status filter', async () => {
    await open()
    byText('[role="radio"], button', 'Suspended')!.click()
    await settle()

    expect(lastQuery().status).toBe('suspended')
    expect(lastQuery().Page).toBe(1)
  })

  it('keeps the status and live choices after the page is opened again', async () => {
    await open()
    byText('[role="radio"], button', 'Suspended')!.click()
    await settle()
    byText('[role="radio"], button', 'Live')!.click()
    await settle()
    mounted!.unmount()
    mounted = null
    document.body.innerHTML = ''
    calls.length = 0

    await open()

    expect(calls[0]!.query.status).toBe('suspended')
    expect(calls[0]!.query.isLive).toBe(true)
    expect(calls[0]!.query.Page).toBe(1)
  })

  it('asks for page 2 when Next is pressed and shows the page count', async () => {
    total = 60
    await open()
    expect(text()).toContain('Page 1 of 3')

    byText('button', 'Next')!.click()
    await settle()

    expect(lastQuery().Page).toBe(2)
    expect(lastQuery().Take).toBe(25)
    expect(text()).toContain('Page 2 of 3')
  })

  it('shows the empty text when no channel matches', async () => {
    rowsOnPage = []
    total = 0
    await open()

    expect(rows()).toHaveLength(0)
    expect(text()).toContain('No channel matches these filters.')
  })

  it('shows the error state with a retry button when the bot refuses the list', async () => {
    refused = true
    await open()

    expect(rows()).toHaveLength(0)
    expect(text()).toContain('You may not see the channel list.')
    const before = calls.length

    refused = false
    byText('button', 'Try again')!.click()
    await settle()

    expect(calls.length).toBeGreaterThan(before)
    expect(rows()).toHaveLength(2)
  })
})

describe('admin channels route', () => {
  const blank = { template: '<div />' }
  const testRouter = (): Router =>
    createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'dashboard-home', component: blank },
        { ...dashboardAdminChannelsRoute, path: '/app/admin/channels' },
      ],
    })

  it('sends a non-admin to the home page', async () => {
    queryClient.setQueryData(['auth', 'me'], { id: 'u1', isAdmin: false })
    const guarded = testRouter()

    await guarded.push('/app/admin/channels')

    expect(guarded.currentRoute.value.name).toBe('dashboard-home')
  })

  it('lets an admin in', async () => {
    queryClient.setQueryData(['auth', 'me'], { id: 'u1', isAdmin: true })
    const guarded = testRouter()

    await guarded.push('/app/admin/channels')

    expect(guarded.currentRoute.value.name).toBe('dashboard-admin-channels')
  })

  it('is registered in the dashboard, so the profile menu link appears', () => {
    const real: Router = createRouter({ history: createMemoryHistory(), routes: [dashboardRoutes] })

    expect(real.hasRoute('dashboard-admin-channels')).toBe(true)
  })
})
