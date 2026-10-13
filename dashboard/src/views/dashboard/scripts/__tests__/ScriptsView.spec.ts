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

import ScriptsView from '../ScriptsView.vue'
import en from '../i18n/en.json'

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> }
  body?: unknown
}
type Call = { method: string; path: string; body: unknown; id: string }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const REFUSAL: string = 'Custom code is switched off for this channel.'

const calls: Call[] = []
let scripts: Record<string, unknown>[] = []
let refused: boolean = false

const script = (
  id: string,
  name: string,
  over: Record<string, unknown> = {},
): Record<string, unknown> => ({
  id,
  name,
  description: `${name} description`,
  isEnabled: true,
  currentVersion: 1,
  currentValidationStatus: 'valid',
  lastRuntimeError: null,
  lastRanAt: '2026-10-12T20:00:00Z',
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  ...over,
})

const fixtures = (): Record<string, unknown>[] => [
  script('s1', 'Hype Train Banner'),
  script('s2', 'Raid Welcome', {
    lastRuntimeError: 'ReferenceError: raider is not defined',
  }),
  script('s3', 'Quote Keeper', { isEnabled: false, lastRanAt: null }),
  script('s4', 'Clip Poster', { isEnabled: false, description: 'Posts new clips' }),
]

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      calls.push({ method: 'GET', path, body: undefined, id: '' })
      if (refused)
        return {
          error: { message: REFUSAL },
          response: { status: 403, ok: false },
        }
      return { data: { data: scripts }, response: { status: 200, ok: true } }
    },
    PATCH: async (path: string, options?: Options): Promise<Answer> => {
      const id: string = String(options?.params?.path?.id)
      calls.push({ method: 'PATCH', path, body: options?.body, id })
      const body: Record<string, unknown> = options?.body as Record<string, unknown>
      scripts = scripts.map((row) => (row.id === id ? { ...row, ...body } : row))
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

beforeEach(() => {
  calls.length = 0
  refused = false
  scripts = fixtures()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
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

async function open(url: string = '/app/scripts'): Promise<void> {
  const blank = { template: '<div />' }
  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/scripts', name: 'dashboard-scripts', component: blank }],
  })
  await router.push(url)
  mounted = mount(ScriptsView, {
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
  ...document.body.querySelectorAll<HTMLElement>('[data-script-row]'),
]
const rowNames = (): string[] =>
  rows().map((row) => row.querySelector('span.font-medium')?.textContent?.trim() ?? '')
const tabCount = (label: string): string =>
  (
    [...document.body.querySelectorAll<HTMLElement>('[role="tab"]')].find((t) =>
      t.textContent?.trim().startsWith(label),
    )?.textContent ?? ''
  )
    .replace(label, '')
    .trim()
const typeSearch = async (value: string): Promise<void> => {
  const input: HTMLInputElement = document.body.querySelector(
    'input[type="search"]',
  ) as HTMLInputElement
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

describe('ScriptsView', () => {
  it('counts each tab from the list, with Failed as the rows that have a runtime error', async () => {
    await open()

    expect(tabCount('All')).toBe('4')
    expect(tabCount('On')).toBe('2')
    expect(tabCount('Off')).toBe('2')
    expect(tabCount('Failed')).toBe('1')
    expect(rowNames()).toEqual(['Hype Train Banner', 'Raid Welcome', 'Quote Keeper', 'Clip Poster'])

    await router.push('/app/scripts?view=failed')
    await settle()
    expect(rowNames()).toEqual(['Raid Welcome'])

    await router.push('/app/scripts?view=off')
    await settle()
    expect(rowNames()).toEqual(['Quote Keeper', 'Clip Poster'])
  })

  it('filters the list by the name or description typed in the search box', async () => {
    await open()

    await typeSearch('raid')
    expect(rowNames()).toEqual(['Raid Welcome'])

    await typeSearch('new clips')
    expect(rowNames()).toEqual(['Clip Poster'])

    await typeSearch('zzz')
    expect(rowNames()).toEqual([])
    expect(text()).toContain('No script matches "zzz".')
  })

  it('switches a script off with a PATCH of isEnabled only and moves the tab counts', async () => {
    await open('/app/scripts?view=on')
    const toggle: HTMLButtonElement = rows()[0]!.querySelector(
      'button[role="switch"]',
    ) as HTMLButtonElement
    expect(toggle.getAttribute('aria-checked')).toBe('true')

    toggle.click()
    await settle()

    const patches: Call[] = calls.filter((call) => call.method === 'PATCH')
    expect(patches).toHaveLength(1)
    expect(patches[0]!.path).toContain('/code-scripts/{id}/enabled')
    expect(patches[0]!.id).toBe('s1')
    expect(patches[0]!.body).toEqual({ isEnabled: false })
    expect(tabCount('On')).toBe('1')
    expect(tabCount('Off')).toBe('3')
    expect(rowNames()).toEqual(['Raid Welcome'])
  })

  it('says Never run when a script has no last run, and Last run when it has', async () => {
    await open()

    expect(rows()[2]!.textContent).toContain('Never run')
    expect(rows()[0]!.textContent).toContain('Last run')
    expect(rows()[0]!.textContent).not.toContain('Never run')
    expect(rows()[1]!.textContent).toContain('Failed')
  })

  it('shows the bot reason, not the empty text, when the bot refuses the list', async () => {
    refused = true
    await open()

    expect(text()).toContain(REFUSAL)
    expect(text()).not.toContain('This channel has no scripts yet.')
    expect(rowNames()).toEqual([])
  })

  it('says so when the channel has no scripts', async () => {
    scripts = []
    await open()

    expect(text()).toContain('This channel has no scripts yet.')
  })
})
