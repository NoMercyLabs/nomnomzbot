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
import { ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'

import OverlaysView from '../OverlaysView.vue'
import en from '../i18n/en.json'

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> }
  body?: unknown
}
type Call = { method: string; path: string; body: unknown }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const SECRET = 'SECRET123'

const calls: Call[] = []
let widgets: Record<string, unknown>[] = []
let listFails: boolean = false

const widget = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  description: `${name} description`,
  framework: 'vue',
  source: 'user',
  isEnabled: true,
  overlayUrl: `https://bot.example/overlay?widgetId=${id}&token=${SECRET}`,
  activeVersionId: null,
  galleryItemId: null,
  settings: {},
  eventSubscriptions: [],
  lastRuntimeError: null,
  lastRanAt: null,
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  galleryUpdateAvailable: false,
  isAttached: false,
  isCustomized: false,
  ...over,
})

const fixtures = (): Record<string, unknown>[] => [
  widget('w1', 'Spotify NowPlaying', {
    source: 'first_party',
    isAttached: true,
    eventSubscriptions: ['spotify.track_changed', 'channel.subscription.gift', 'follow'],
  }),
  widget('w2', 'Feather Alert', {
    lastRuntimeError: 'TypeError: feather is undefined',
    lastRanAt: '2026-10-12T20:00:00Z',
  }),
  widget('w3', 'Audio', { isEnabled: false }),
  widget('w4', 'Chat Box', { isEnabled: false, overlayUrl: null, isCustomized: true }),
]

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path, body: undefined })

      if (listFails)
        return { error: { status: 'error' }, response: { status: 500, ok: false } }

      return {
        data: { data: widgets, hasMore: false, nextPage: null },
        response: { status: 200, ok: true },
      }
    },
    PUT: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({ method: 'PUT', path, body: options?.body })
      const id = String(options?.params?.path?.widgetId)
      const body = options?.body as Record<string, unknown>

      widgets = widgets.map((row) =>
        row.id === id
          ? { ...row, ...body }
          : row)

      return { data: { data: null }, response: { status: 200, ok: true } }
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let pinia: Pinia
let router: Router
const written: string[] = []

beforeEach(() => {
  calls.length = 0
  written.length = 0
  listFails = false
  widgets = fixtures()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)

  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: {
      writeText: async (text: string): Promise<void> => {
        written.push(text)
      },
    },
  })
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(url: string = '/app/overlays'): Promise<void> {
  const blank = { template: '<div />' }

  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/overlays', name: 'dashboard-overlays', component: blank }],
  })

  await router.push(url)

  mounted = mount(OverlaysView, {
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
const rowNames = (): string[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-overlay-row]')].map(
    (row) => row.querySelector('span.font-medium')?.textContent?.trim() ?? '',
  )
const tabCount = (label: string): string =>
  (
    [...document.body.querySelectorAll<HTMLElement>('[role="tab"]')].find((t) =>
      t.textContent?.trim().startsWith(label),
    )?.textContent ?? ''
  )
    .replace(label, '')
    .trim()
const detail = (): HTMLElement =>
  document.body.querySelector('[data-overlay-detail]') as HTMLElement
const button = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
    b.textContent?.includes(label),
  ) as HTMLButtonElement

const typeSearch = async (value: string): Promise<void> => {
  const input = document.body.querySelector('input[type="search"]') as HTMLInputElement

  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

describe('OverlaysView', () => {
  it('counts each tab from the list and filters the list by it', async () => {
    await open()

    expect(tabCount('All')).toBe('4')
    expect(tabCount('On')).toBe('2')
    expect(tabCount('Off')).toBe('2')
    expect(tabCount('Has a problem')).toBe('1')
    expect(rowNames()).toEqual(['Spotify NowPlaying', 'Feather Alert', 'Audio', 'Chat Box'])

    await router.push('/app/overlays?view=on')
    await settle()
    expect(rowNames()).toEqual(['Spotify NowPlaying', 'Feather Alert'])

    await router.push('/app/overlays?view=off')
    await settle()
    expect(rowNames()).toEqual(['Audio', 'Chat Box'])

    await router.push('/app/overlays?view=problem')
    await settle()
    expect(rowNames()).toEqual(['Feather Alert'])
  })

  it('filters the list by the name typed in the search box', async () => {
    await open()
    await typeSearch('feath')

    expect(rowNames()).toEqual(['Feather Alert'])
  })

  it('says which overlays are on, off, attached or failing in the row', async () => {
    await open()

    const rows: HTMLElement[] = [
      ...document.body.querySelectorAll<HTMLElement>('[data-overlay-row]'),
    ]

    expect(rows[0]!.textContent).toContain('A browser source is open')
    expect(rows[1]!.textContent).toContain('Fails')
    expect(rows[2]!.textContent).toContain('Off')
    expect(rows[0]!.textContent).not.toContain('Fails')
  })

  it('shows the overlay named in the address, with its kind, origin, events and description', async () => {
    await open('/app/overlays?id=w1')

    const head = detail().textContent ?? ''

    expect(detail().querySelector('h2')?.textContent).toBe('Spotify NowPlaying')
    expect(head).toContain('Built in')
    expect(head).toContain('vue')
    expect(head).toContain('Spotify NowPlaying description')
    expect(head).toContain('track changed')
    expect(head).toContain('spotify.track_changed')
    expect(head).toContain('subscription gift')
    expect(head).toContain('Reacts to 3 events')
  })

  // A key that is already plain ("follow") shows once, not as a name with the same word under it.
  it('names an event once when its key is already a plain word', async () => {
    await open('/app/overlays?id=w1')

    // The row's name text only; the Test button beside it is not part of the name.
    const rows: string[] = [...detail().querySelectorAll('[data-event]')].map((row: Element) =>
      (row.querySelector(':scope > span:not([data-slot])')?.textContent ?? '')
        .replace(/\s+/g, ' ')
        .trim(),
    )

    expect(rows).toContain('follow')
    expect(rows).toContain('track changedspotify.track_changed')
  })

  it('opens the overlay of a row when it is picked', async () => {
    await open()

    ;(
      document.body.querySelectorAll<HTMLElement>('[data-overlay-row] button')[2] as HTMLElement
    ).click()

    await settle()

    expect(router.currentRoute.value.query.id).toBe('w3')
    expect(detail().querySelector('h2')?.textContent).toBe('Audio')
  })

  it('switches an overlay off with a PUT of isEnabled only and moves it from the On tab to the Off tab', async () => {
    await open('/app/overlays?id=w1&view=on')
    const toggle = detail().querySelector('button[role="switch"]') as HTMLButtonElement

    expect(toggle.getAttribute('aria-checked')).toBe('true')

    toggle.click()
    await settle()

    const puts = calls.filter((call) => call.method === 'PUT')

    expect(puts).toHaveLength(1)
    expect(puts[0]!.path).toContain('/widgets/{widgetId}')
    expect(puts[0]!.body).toEqual({ isEnabled: false })
    expect(tabCount('On')).toBe('1')
    expect(tabCount('Off')).toBe('3')
    expect(rowNames()).toEqual(['Feather Alert'])
  })

  it('copies the overlay address to the clipboard and never prints the token', async () => {
    await open('/app/overlays?id=w1')
    expect(text()).not.toContain(SECRET)

    button('Copy browser source address').click()
    await settle()

    expect(written).toEqual([`https://bot.example/overlay?widgetId=w1&token=${SECRET}`])
    expect(text()).toContain('Address copied')
    expect(text()).not.toContain(SECRET)
  })

  it('disables the copy button with a reason when the overlay has no address', async () => {
    await open('/app/overlays?id=w4')

    const copy = button('Copy browser source address')

    expect(copy.disabled).toBe(true)
    expect(detail().textContent).toContain('This overlay has no address yet.')
    expect(detail().textContent).toContain('Edited')
  })

  it('shows the failure text in Health and a Fails badge on the row', async () => {
    await open('/app/overlays?id=w2')

    const health = detail().querySelector('[data-slot="overlay-health"]') as HTMLElement

    expect(health.textContent).toContain('TypeError: feather is undefined')
    expect(health.textContent).toContain('Failed')
    expect(rowNames()).toContain('Feather Alert')
    expect(document.body.querySelectorAll('[data-overlay-row]')[1]!.textContent).toContain('Fails')
  })

  it('says a working overlay works, and a switched-off one is switched off', async () => {
    await open('/app/overlays?id=w1')
    expect(detail().querySelector('[data-slot="overlay-health"]')!.textContent).toContain('Works')

    expect(detail().querySelector('[data-slot="overlay-health"]')!.textContent).toContain(
      'A browser source is open',
    )

    await router.push('/app/overlays?id=w3')
    await settle()

    expect(detail().querySelector('[data-slot="overlay-health"]')!.textContent).toContain(
      'Switched off',
    )
  })

  it('says so when the bot has no overlays', async () => {
    widgets = []
    await open()

    expect(rowNames()).toEqual([])
    expect(text()).toContain('This channel has no overlays yet.')
  })

  it('shows an error with a retry when the list cannot load', async () => {
    listFails = true
    await open()

    expect(text()).toContain('The overlays could not load.')
    listFails = false
    button('Try again').click()
    await settle()

    expect(rowNames()).toHaveLength(4)
  })
})
