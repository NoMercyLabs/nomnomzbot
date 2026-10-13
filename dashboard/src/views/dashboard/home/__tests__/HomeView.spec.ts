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

import { DEFAULT_UNDO_MS } from '@/composables/useInlineFlow'
import { i18n } from '@/i18n'
import { useSaveErrorsStore } from '@/stores/saveErrors'

import en from '../i18n/en.json'
import HomeView from '../HomeView.vue'

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown }
type Call = { method: string; path: string; body: unknown }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}
let resolveFails = false

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (): Answer => ({ error: { status: 'error' }, response: { status: 403, ok: false } })

const liveStats = {
  isLive: true,
  streamTitle: 'Building the bot',
  gameName: 'Software and Game Development',
  viewerCount: 20,
  followerCount: 346,
  subscriberCount: 12,
  platformsLive: ['twitch'],
  uptime: 3725,
}

let queue: Record<string, unknown>[] = []

const heldMessage = (id: string, name: string, message: string): Record<string, unknown> => ({
  id,
  source: 'automod',
  status: 'pending',
  targetTwitchUserId: `t-${id}`,
  targetUsernameSnapshot: name,
  messageContentSnapshot: message,
  autoModCategory: 'swearing',
  createdAt: '2026-10-13T10:00:00Z',
  resolvedAt: null,
  resolvedByName: null,
  resolutionAction: null,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/stats': () => ok(liveStats),
    '/automod/queue': () => ok(queue.map((item) => ({ ...item }))),
    '/integrations/status': () =>
      ok([
        {
          provider: 'twitch',
          connected: true,
          accountName: 'Stoney_Eagle',
          needsReauth: false,
          grantedScopeSets: [],
          capabilities: {},
          loginOnly: false,
        },
        {
          provider: 'kick',
          connected: false,
          accountName: null,
          needsReauth: false,
          grantedScopeSets: [],
          capabilities: {},
          loginOnly: false,
        },
        {
          provider: 'spotify',
          connected: true,
          accountName: 'stoney',
          needsReauth: true,
          grantedScopeSets: [],
          capabilities: {},
          loginOnly: false,
        },
      ]),
    '/bot/status': () =>
      ok({
        connected: true,
        login: 'nomercybot_',
        displayName: 'NoMercyBot_',
        profileImageUrl: null,
      }),
    '/activity': () =>
      ok([
        {
          id: 'e1',
          type: 'channel.follow',
          userId: 'u9',
          username: 'StanLeFresh',
          data: null,
          timestamp: '2026-10-13T09:00:00Z',
        },
      ]),
    '/action-required': () =>
      ok([
        {
          id: 'a1',
          titleKey: 'nothing.known',
          messageKey: 'nothing.known',
          deepLinkRoute: '/app/settings',
          severity: 'warning',
        },
      ]),
    '/moderation/shield': () => ok({ enabled: false }),
  }
}

function answer(method: string, path: string, options: Options | undefined): Answer {
  calls.push({ method, path, body: options?.body })
  const match: string | undefined = Object.keys(answers).find((suffix) => path.endsWith(suffix))
  return match ? answers[match]!() : ok(null)
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => answer('GET', path, options),
    POST: async (path: string, options?: Options) => {
      calls.push({ method: 'POST', path, body: options?.body })
      if (resolveFails) return refused()
      const itemId: unknown = options?.params?.path?.queueItemId
      queue = queue.filter((item) => item.id !== itemId)
      return { response: { status: 200, ok: true } }
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let pinia: Pinia

beforeEach(() => {
  calls.length = 0
  queue = [
    heldMessage('q1', 'sample_viewer', 'what the heck'),
    heldMessage('q2', 'DukaSoft', 'buy followers here'),
  ]
  answers = freshAnswers()
  resolveFails = false
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.useRealTimers()
})

function makeRouter(): Router {
  const blank = { template: '<div />' }
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/app', name: 'dashboard-home', component: blank },
      { path: '/app/chat', name: 'dashboard-chat', component: blank },
      { path: '/app/stream', name: 'dashboard-stream', component: blank },
    ],
  })
}

async function open(): Promise<void> {
  const router: Router = makeRouter()
  await router.push('/app')
  mounted = mount(HomeView, {
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

const text = (): string => document.body.textContent ?? ''
const panel = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]')].find((card) =>
    card.querySelector('h2')?.textContent?.includes(title),
  ) as HTMLElement
const rowOf = (name: string): HTMLElement =>
  [...panel('Review queue').querySelectorAll<HTMLElement>('[data-queue-item]')].find((row) =>
    row.textContent?.includes(name),
  ) as HTMLElement
const buttonIn = (root: HTMLElement, label: string): HTMLButtonElement =>
  [...root.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLButtonElement
const resolves = (): Call[] =>
  calls.filter((call) => call.method === 'POST' && call.path.endsWith('/resolve'))

describe('HomeView', () => {
  it('shows a live stream: the live badge, the stream line and the held messages to review', async () => {
    await open()

    expect(text()).toContain('Live on Twitch')
    expect(text()).toContain('20 viewers')
    expect(text()).toContain('346 followers')
    expect(panel('Review queue').textContent).toContain('2 waiting')
    expect(rowOf('sample_viewer').textContent).toContain('what the heck')
    expect(rowOf('DukaSoft').textContent).toContain('buy followers here')
    expect(panel('Happening now').textContent).toContain('StanLeFresh')
    expect(panel('Happening now').textContent).toContain('Followed')
    expect(document.body.querySelector('a[href="/app/chat"]')?.textContent).toContain('Open chat')
  })

  it('shows each happening row with its icon, readable detail and weekday plus 24h time', async () => {
    const at = (type: string, username: string | null, data: unknown): Record<string, unknown> => ({
      id: `${type}-${username}`,
      type,
      userId: 'u1',
      username,
      data: JSON.stringify(data),
      timestamp: '2026-10-10T21:15:00',
    })
    answers['/activity'] = () =>
      ok([
        at('channel.subscription.end', 'night_light90', { tier: '1000', isGift: true }),
        at('channel.raid.out', null, { user: 'JeroenLabs', viewers: '16' }),
        at('channel.raid', 'CptTree', { viewers: 6 }),
        at('channel.subscription.gift', 'Gifter', { giftCount: 5, tier: '1000' }),
        at('channel.something.new', 'Odd', null),
      ])
    await open()

    const rows: HTMLElement[] = [
      ...panel('Happening now').querySelectorAll<HTMLElement>('[data-activity-row]'),
    ]
    const lines: string[] = rows.map((row) => row.textContent ?? '')
    expect(lines[0]).toContain('night_light90')
    expect(lines[0]).toContain('Gift sub ended · tier 1')
    expect(lines[0]).toMatch(/Sat,? 21:15/)
    expect(lines[1]).toContain('Raided JeroenLabs')
    expect(lines[1]).toContain('16 viewers went along')
    expect(lines[2]).toContain('Raided with 6 viewers')
    expect(lines[3]).toContain('Gifted 5 subs · tier 1')
    expect(lines[4]).toContain('Channel event')
    expect(lines.join(' ')).not.toContain('channel.')
    for (const row of rows) expect(row.querySelector('svg')).not.toBeNull()
    // No events page exists yet, so no "All events" link points somewhere else.
    expect(panel('Happening now').textContent).not.toContain('All events')
  })

  it('shows an offline stream: no live badge, the offline reason in the queue and the last stream', async () => {
    answers['/stats'] = () => ok({ ...liveStats, isLive: false, uptime: null, platformsLive: [] })
    queue = []
    await open()

    expect(text()).toContain('Offline')
    expect(text()).not.toContain('Live on')
    expect(panel('Review queue').textContent).toContain(
      'Held messages and spam checks show up here during a stream.',
    )
    expect(panel('Last stream').textContent).toContain('StanLeFresh')
  })

  it('allows a held message at once: it leaves the queue and the server gets approve', async () => {
    await open()

    buttonIn(rowOf('sample_viewer'), 'Allow').click()
    await settle()

    expect(resolves()).toHaveLength(1)
    expect(resolves()[0]!.body).toEqual({ action: 'approve' })
    expect(rowOf('sample_viewer')).toBeUndefined()
    expect(panel('Review queue').textContent).toContain('1 waiting')
  })

  it('holds a deny for the undo time, so Undo means nothing reached Twitch', async () => {
    await open()
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })

    buttonIn(rowOf('DukaSoft'), 'Deny').click()
    await flushPromises()
    expect(resolves()).toHaveLength(0)
    expect(text()).toContain('Denied')

    buttonIn(panel('Review queue'), 'Undo').click()
    await flushPromises()
    vi.advanceTimersByTime(DEFAULT_UNDO_MS + 1000)
    await flushPromises()

    expect(resolves()).toHaveLength(0)
    expect(rowOf('DukaSoft')).toBeDefined()
  })

  it('sends a deny once the undo time has passed, and the message leaves the queue', async () => {
    await open()
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })

    buttonIn(rowOf('DukaSoft'), 'Deny').click()
    await flushPromises()
    vi.advanceTimersByTime(DEFAULT_UNDO_MS)
    await flushPromises()
    vi.useRealTimers()
    await settle()

    expect(resolves()).toHaveLength(1)
    expect(resolves()[0]!.body).toEqual({ action: 'deny' })
    expect(rowOf('DukaSoft')).toBeUndefined()
  })

  it('puts a message back and reports it when the server refuses the resolve', async () => {
    resolveFails = true
    await open()

    buttonIn(rowOf('sample_viewer'), 'Allow').click()
    await settle()

    expect(rowOf('sample_viewer')).toBeDefined()
    expect(useSaveErrorsStore().items).toHaveLength(1)
  })

  it('says why the review queue is missing when the server refuses it', async () => {
    answers['/automod/queue'] = refused
    await open()

    expect(panel('Review queue').querySelector('[data-slot="panel-error"]')?.textContent).toContain(
      'The review queue could not load.',
    )
  })

  it('shows each connection as it is: connected, not connected, or needing a new sign-in', async () => {
    await open()
    const connections: string = panel('Connections').textContent ?? ''

    expect(connections).toContain('Stoney_Eagle')
    expect(connections).toContain('Not connected')
    expect(connections).toContain('Sign in again')
    expect(connections).toContain('NoMercyBot_')
  })
})
