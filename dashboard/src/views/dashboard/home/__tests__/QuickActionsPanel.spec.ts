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
import { useSaveErrorsStore } from '@/stores/saveErrors'

import en from '../i18n/en.json'
import QuickActionsPanel from '../components/QuickActionsPanel.vue'

type Call = { method: string; path: string }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (): Answer => ({ error: { status: 'error' }, response: { status: 500, ok: false } })
const nowSeconds = (): number => Math.floor(Date.now() / 1000)

let snoozeCount = 2
let snoozeFails = false

const schedule = (): Record<string, number> => ({
  snoozeCount,
  snoozeRefreshAt: nowSeconds() + 1172,
  nextAdAt: nowSeconds() + 754,
  duration: 180,
  lastAdAt: 0,
  prerollFreeTime: 0,
})

const timer = (
  name: string,
  intervalMinutes: number,
  isEnabled: boolean,
  firedMinutesAgo: number,
): Record<string, unknown> => ({
  id: name,
  name,
  intervalMinutes,
  isEnabled,
  fireOnce: false,
  lastFiredAt: new Date(Date.now() - firedMinutesAgo * 60_000).toISOString(),
  messageCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
})

const queueItem = (position: number, requestedBy: string | null): Record<string, unknown> => ({
  position,
  trackName: `Track ${position}`,
  artist: 'Artist',
  imageUrl: null,
  durationMs: 1000,
  requestedBy,
  cost: 0,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/ads/schedule': () => ok(schedule()),
    '/music/queue': () =>
      ok({
        nowPlaying: null,
        queue: [queueItem(1, 'DukaSoft'), queueItem(2, 'DukaSoft'), queueItem(3, 'DukaSoft')],
      }),
    '/timers': () =>
      ok([
        timer('discord', 10, true, 6),
        timer('socials', 30, true, 5),
        timer('lurk', 20, true, 2),
        timer('old', 5, false, 1),
      ]),
    '/live-ops/polls': () => ok([]),
    '/live-ops/predictions': () => ok([]),
  }
}

function answer(method: string, path: string): Answer {
  calls.push({ method, path })
  const match = Object.keys(answers).find((suffix) => path.endsWith(suffix))

  return match
    ? answers[match]!()
    : ok(null)
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer('GET', path),
    POST: async (path: string) => {
      calls.push({ method: 'POST', path })

      if (snoozeFails)
        return refused()

      if (path.endsWith('/ads/snooze')) {
        snoozeCount -= 1
      }

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
  snoozeCount = 2
  snoozeFails = false
  answers = freshAnswers()
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function open(): Promise<void> {
  const blank = { template: '<div />' }

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/app', name: 'dashboard-home', component: blank },
      { path: '/app/music', name: 'dashboard-music', component: blank },
      { path: '/app/stream', name: 'dashboard-stream', component: blank },
    ],
  })

  await router.push('/app')

  mounted = mount(QuickActionsPanel, {
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

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

const row = (name: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-quick-row="${name}"]`) as HTMLElement
const buttonIn = (root: HTMLElement, label: string): HTMLElement =>
  [...root.querySelectorAll<HTMLElement>('button, a')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLElement
const posts = (): Call[] => calls.filter((call) => call.method === 'POST')

describe('QuickActionsPanel', () => {
  it('shows each row with the values the server gave', async () => {
    await open()

    expect(document.body.textContent).toContain('Quick actions')

    expect(row('ad').querySelector('[data-slot="countdown-time"]')?.textContent).toMatch(
      /^12:\d\d$/,
    )

    expect(row('ad').textContent).toContain('3 min')
    expect(row('ad').textContent).toContain('2 snoozes left')
    expect(row('music').textContent).toContain('3 waiting')
    expect(row('music').textContent).toContain('All sent by DukaSoft')
    expect(row('timers').textContent).toContain('3 timers on')
    expect(row('timers').textContent).toContain('Next timer: discord')
    expect(row('timers').textContent).not.toMatch(/\d:\d\d/)
    expect(row('poll').textContent).toContain('Poll or prediction')
  })

  it('links the song queue to the music page', async () => {
    await open()

    buttonIn(row('music'), 'Open queue').click()
    await settle()

    expect(router.currentRoute.value.name).toBe('dashboard-music')
  })

  it('snoozes once: one call, and the row shows one snooze fewer', async () => {
    await open()

    buttonIn(row('ad'), 'Snooze 5 min').click()
    await settle()

    expect(posts()).toEqual([
      { method: 'POST', path: expect.stringContaining('/live-ops/ads/snooze') },
    ])

    expect(row('ad').textContent).toContain('1 snooze left')
  })

  it('turns Snooze off when no snooze is left', async () => {
    snoozeCount = 0
    await open()

    expect(row('ad').textContent).toContain('No snoozes left')
    expect((buttonIn(row('ad'), 'Snooze 5 min') as HTMLButtonElement).disabled).toBe(true)
  })

  it('reports a refused snooze and puts the count back', async () => {
    snoozeFails = true
    await open()

    buttonIn(row('ad'), 'Snooze 5 min').click()
    await settle()

    expect(useSaveErrorsStore().items).toHaveLength(1)
    expect(row('ad').textContent).toContain('2 snoozes left')
  })

  it('says plainly that no poll or prediction runs, with no start buttons', async () => {
    await open()

    expect(row('poll').textContent).toContain('No poll or prediction running')
    expect(row('poll').querySelectorAll('button, a')).toHaveLength(0)
  })

  it('shows the time left of a running poll', async () => {
    answers['/live-ops/polls'] = () =>
      ok([
        {
          id: 'p1',
          title: 'Which game next?',
          status: 'ACTIVE',
          duration: 300,
          startedAt: new Date().toISOString(),
          endedAt: null,
          choices: [],
        },
        {
          id: 'p0',
          title: 'Old poll',
          status: 'COMPLETED',
          duration: 300,
          startedAt: '2026-01-01T00:00:00Z',
          endedAt: '2026-01-01T00:05:00Z',
          choices: [],
        },
      ])

    await open()

    expect(row('poll').textContent).toContain('Which game next?')
    expect(row('poll').textContent).not.toContain('Old poll')

    expect(row('poll').querySelector('[data-slot="countdown-time"]')?.textContent).toMatch(
      /^[45]:\d\d$/,
    )

    expect(row('poll').textContent).not.toContain('No poll or prediction running')
  })

  it('shows a locked prediction as locked, with no clock', async () => {
    answers['/live-ops/predictions'] = () =>
      ok([
        {
          id: 'x1',
          title: 'Will it build?',
          status: 'LOCKED',
          predictionWindow: 120,
          createdAt: new Date().toISOString(),
          endedAt: null,
          lockedAt: new Date().toISOString(),
          outcomes: [],
        },
      ])

    await open()

    expect(row('poll').textContent).toContain('Will it build?')
    expect(row('poll').textContent).toContain('Locked')
    expect(row('poll').querySelector('[data-slot="countdown-time"]')).toBeNull()
  })

  it('shows its own error line for a row whose source failed, and the others still render', async () => {
    answers['/music/queue'] = refused
    await open()

    expect(row('music').textContent).toContain('The song queue could not load.')
    expect(row('music').textContent).not.toContain('waiting')
    expect(row('ad').textContent).toContain('2 snoozes left')
    expect(row('timers').textContent).toContain('3 timers on')
  })

  it('invents nothing: empty sources say so and show no numbers', async () => {
    answers['/ads/schedule'] = () => ok(null)
    answers['/music/queue'] = () => ok({ nowPlaying: null, queue: [] })
    answers['/timers'] = () => ok([])
    await open()

    expect(row('ad').textContent).toContain('No ad break is scheduled.')
    expect(row('ad').querySelector('[data-slot="countdown-time"]')).toBeNull()
    expect(row('music').textContent).toContain('The queue is empty.')
    expect(row('music').textContent).not.toContain('waiting')
    expect(row('timers').textContent).toContain('No timers are on.')
  })
})
