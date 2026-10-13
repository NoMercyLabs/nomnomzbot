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
import StreamView from '../StreamView.vue'

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> }
  body?: unknown
}
type Call = { method: string; path: string; body: unknown; query: unknown; pathParams: unknown }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}
let saveRefused = false

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (): Answer => ({ error: { status: 'error' }, response: { status: 403, ok: false } })

const streamInfo = {
  title: 'Building the bot',
  gameName: 'Software and Game Development',
  tags: ['coding', 'English'],
  isLive: false,
  viewerCount: 0,
  startedAt: null,
  language: 'en',
  lastStreamedAt: '2026-10-10T20:00:00Z',
}

const pastStream = (id: string, title: string, game: string): Record<string, unknown> => ({
  streamId: id,
  title,
  gameName: game,
  startedAt: '2026-10-10T18:00:00Z',
  endedAt: '2026-10-10T20:30:00Z',
  durationSeconds: 9000,
  peakViewers: 31,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/stream': () => ok({ ...streamInfo }),
    '/analytics/streams': () =>
      ok([
        pastStream('s1', 'Fixing the queue', 'Just Chatting'),
        pastStream('s2', 'Building the bot', 'Software and Game Development'),
      ]),
    '/stream/categories': () =>
      ok([
        { id: '1', name: 'Software and Game Development', boxArtUrl: '' },
        { id: '2', name: 'Just Chatting', boxArtUrl: '' },
      ]),
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
      ]),
    '/bot/status': () =>
      ok({
        connected: true,
        login: 'nomercybot_',
        displayName: 'NoMercyBot_',
        profileImageUrl: null,
      }),
    '/rewards': () =>
      ok([
        { id: 'r1', title: 'Hydrate', isEnabled: true, isPaused: false },
        { id: 'r2', title: 'Song request', isEnabled: false, isPaused: false },
        { id: 'r3', title: 'Pick a game', isEnabled: true, isPaused: true },
      ]),
    '/moderation/shield': () => ok({ enabled: false }),
    '/integrations/{provider}/connect': () =>
      ok({ authorizeUrl: 'https://id.kick.com/oauth?x=1', state: 's' }),
  }
}

function answer(method: string, path: string, options: Options | undefined): Answer {
  calls.push({
    method,
    path,
    body: options?.body,
    query: options?.params?.query,
    pathParams: options?.params?.path,
  })
  const match = Object.keys(answers)
    .sort((a, b) => b.length - a.length)
    .find((suffix) => path.endsWith(suffix))

  return match
    ? answers[match]!()
    : ok(null)
}

vi.mock('@/api/client', () =>
  ({
    client: {
      use: () => undefined,
      GET: async (path: string, options?: Options) => answer('GET', path, options),
      POST: async (path: string, options?: Options) => answer('POST', path, options),
      PUT: async (path: string, options?: Options) => {
        calls.push({
          method: 'PUT',
          path,
          body: options?.body,
          query: undefined,
          pathParams: options?.params?.path,
        })

        return saveRefused
          ? refused()
          : {
              data: { data: { ...streamInfo, ...(options?.body as object) } },
              response: { status: 200, ok: true },
            }
      },
    },
  }))

vi.mock('@/composables/useCurrentChannel', () =>
  ({
    useCurrentChannel: () => ({
      channelId: ref('c1'),
      current: ref({ login: 'stoney_eagle', displayName: 'Stoney_Eagle', isLive: false }),
    }),
  }))

const assign = vi.fn<(url: string) => void>()

let mounted: VueWrapper | null = null
let pinia: Pinia

beforeEach(() => {
  calls.length = 0
  answers = freshAnswers()
  saveRefused = false
  assign.mockClear()
  vi.stubGlobal('location', { ...window.location, assign })
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

async function open(): Promise<void> {
  const blank = { template: '<div />' }
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/stream', name: 'dashboard-stream', component: blank }],
  })

  await router.push('/app/stream')
  mounted = mount(StreamView, {
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
const titleBox = (): HTMLTextAreaElement =>
  document.body.querySelector<HTMLTextAreaElement>('#stream-title') as HTMLTextAreaElement
const categoryBox = (): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(
    '[data-field="category-search"]',
  ) as HTMLInputElement
const tagBox = (): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>('[data-field="tag-input"]') as HTMLInputElement
const button = (label: string): HTMLButtonElement | undefined =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  )
const puts = (): Call[] => calls.filter((call) => call.method === 'PUT')

async function type(box: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  box.value = value
  box.dispatchEvent(new Event('input', { bubbles: true }))
  await settle()
}

async function pressEnter(box: HTMLInputElement): Promise<void> {
  box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
  await settle()
}

describe('the stream info form', () => {
  it('shows what the channel has now and keeps Save off until something changes', async () => {
    await open()

    expect(titleBox().value).toBe('Building the bot')
    expect(text()).toContain('16 / 140')
    expect(text()).toContain('Software and Game Development')
    expect(text()).toContain('coding')
    expect(text()).toContain('English')
    expect(button('Save changes')?.disabled).toBe(true)
  })

  it('sends the whole stream info with the new title and then has nothing left to save', async () => {
    await open()

    await type(titleBox(), 'Fixing the stream page')
    expect(button('Save changes')?.disabled).toBe(false)
    button('Save changes')?.click()
    await settle()

    expect(puts()).toHaveLength(1)
    expect(puts()[0]!.path.endsWith('/channels/{channelId}/stream')).toBe(true)
    expect(puts()[0]!.pathParams).toMatchObject({ channelId: 'c1' })
    expect(puts()[0]!.body).toEqual({
      title: 'Fixing the stream page',
      gameName: 'Software and Game Development',
      tags: ['coding', 'English'],
    })
    expect(titleBox().value).toBe('Fixing the stream page')
    expect(button('Save changes')?.disabled).toBe(true)
  })

  it('cuts a title at 140 characters', async () => {
    await open()

    await type(titleBox(), 'x'.repeat(150))

    expect(titleBox().value).toHaveLength(140)
    expect(text()).toContain('140 / 140')
  })

  it('keeps the typed title and reports the error when the save is refused', async () => {
    saveRefused = true
    await open()

    await type(titleBox(), 'Fixing the stream page')
    button('Save changes')?.click()
    await settle()

    expect(titleBox().value).toBe('Fixing the stream page')
    expect(button('Save changes')?.disabled).toBe(false)
    expect(useSaveErrorsStore().items).toHaveLength(1)
  })

  it('Discard puts the server values back without a request', async () => {
    await open()

    await type(titleBox(), 'Something else')
    button('Discard')?.click()
    await settle()

    expect(titleBox().value).toBe('Building the bot')
    expect(puts()).toHaveLength(0)
  })

  it('fills the title from a recent title', async () => {
    await open()

    const row = [...document.body.querySelectorAll<HTMLElement>('[data-recent-title]')].find((el) =>
      el.textContent?.includes('Fixing the queue'),
    )

    row?.click()
    await settle()

    expect(titleBox().value).toBe('Fixing the queue')
  })

  it('searches categories on the server and saves the chosen one', async () => {
    await open()

    await type(categoryBox(), 'just')
    await new Promise((done) => setTimeout(done, 400))
    await settle()
    const search = calls.find((call) => call.path.endsWith('/stream/categories'))

    expect(search?.query).toEqual({ query: 'just' })

    const result = [...document.body.querySelectorAll<HTMLElement>('[data-category-result]')].find(
      (el) => el.textContent?.includes('Just Chatting'),
    )

    result?.click()
    await settle()
    button('Save changes')?.click()
    await settle()

    expect(puts()[0]!.body).toEqual({
      title: 'Building the bot',
      gameName: 'Just Chatting',
      tags: ['coding', 'English'],
    })
  })

  it('adds a tag with Enter, removes one, and brings it back with Undo', async () => {
    await open()

    await type(tagBox(), 'speedrun')
    await pressEnter(tagBox())
    expect(text()).toContain('speedrun')

    document.body.querySelector<HTMLButtonElement>('[aria-label="Remove coding"]')?.click()
    await settle()
    expect(text()).not.toContain('Remove coding')
    expect(text()).toContain('Removed coding.')

    button('Undo')?.click()
    await settle()
    expect(document.body.querySelector('[aria-label="Remove coding"]')).not.toBeNull()

    button('Save changes')?.click()
    await settle()
    expect(puts()[0]!.body).toMatchObject({ tags: ['coding', 'English', 'speedrun'] })
  })

  it('stops at 10 tags', async () => {
    answers['/stream'] = () =>
      ok({ ...streamInfo, tags: ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j'] })
    await open()

    await type(tagBox(), 'extra')
    await pressEnter(tagBox())

    expect(document.body.querySelector('[aria-label="Remove extra"]')).toBeNull()
    expect(text()).toContain('10 of 10 tags')
  })

  it('shows the content labels as switches that cannot be changed yet', async () => {
    await open()

    const labels: HTMLElement[] = [
      ...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"]'),
    ]

    expect(labels).toHaveLength(7)
    expect(
      labels.every(
        (row) =>
          row.querySelector('[role="switch"][disabled], [role="switch"][data-disabled]') !== null,
      ),
    ).toBe(true)
  })
})

describe('the page header', () => {
  it('says when the last stream ended and how long it ran while offline', async () => {
    await open()

    expect(text()).toContain('Offline')
    expect(text()).toContain('Last stream ended')
    expect(text()).toContain('2h 30m')
  })

  // The live server leaves endedAt and durationSeconds out of a stream it never closed.
  it('gives only the date when the last stream has no end or length', async () => {
    answers['/analytics/streams'] = () =>
      ok([{ streamId: 's1', title: '', gameName: '', startedAt: '2026-08-30T11:11:07+00:00' }])
    await open()

    expect(text()).not.toContain('NaN')
    expect(text()).not.toContain('Last stream ended')
    expect(text()).toContain('Last streamed')
  })
})

describe('where it goes', () => {
  it('lists Twitch as connected, Kick as not connected and X Live as not available', async () => {
    await open()

    expect(text()).toContain('Stoney_Eagle')
    expect(text()).toContain('Not connected')
    expect(text()).toContain('Not available yet')
  })

  it('asks the server for the Kick authorize link and goes there after one confirm', async () => {
    await open()

    document.body.querySelector<HTMLButtonElement>('[data-platform="kick"] button')?.click()
    await settle()
    document.body.querySelector<HTMLButtonElement>('[data-flow-act="ok"]')?.click()
    await settle()

    const connect = calls.find(
      (call) => call.method === 'POST' && call.path.endsWith('/integrations/{provider}/connect'),
    )

    expect(connect?.pathParams).toMatchObject({ channelId: 'c1', provider: 'kick' })
    expect(connect?.body).toMatchObject({ scopeSetKey: 'kick.chat' })
    expect(assign).toHaveBeenCalledWith('https://id.kick.com/oauth?x=1')
  })
})

describe('before you go live', () => {
  it('counts the rewards that are on and names the shield state', async () => {
    await open()

    expect(text()).toContain('Title and category are set')
    expect(text()).toContain('2 tags added')
    expect(text()).toContain('The bot is in chat')
    expect(text()).toContain('Chat is open to everyone')
  })

  // A reward the streamer switched off is a choice, not a fault: it is named with a grey dot, never red (mockup 10-stream.html:157-158).
  it('names each reward that is off, with the on count, and never marks it as an error', async () => {
    await open()

    const offRows: HTMLElement[] = [
      ...document.body.querySelectorAll<HTMLElement>('[data-check^="reward-"]'),
    ]

    expect(offRows.map((row) => row.textContent?.trim())).toEqual([
      'Song request is off. 1 of 3 rewards are on.',
      'Pick a game is off. 1 of 3 rewards are on.',
    ])
    expect(offRows.every((row) => row.querySelector('[data-tone="off"]') !== null)).toBe(true)
  })

  it('says all rewards are on when none is off', async () => {
    answers['/rewards'] = () =>
      ok([{ id: 'r1', title: 'Hydrate', isEnabled: true, isPaused: false }])
    await open()

    expect(text()).toContain('All 1 rewards are on')
    expect(document.body.querySelector('[data-check^="reward-"]')).toBeNull()
  })

  it('warns when the title is empty', async () => {
    answers['/stream'] = () => ok({ ...streamInfo, title: '' })
    await open()

    expect(text()).toContain('Set a title and a category')
  })

  it('says which check failed instead of showing a wrong answer', async () => {
    answers['/rewards'] = refused
    await open()

    expect(text()).toContain('Could not check the rewards')
    expect(text()).not.toContain('rewards are on')
    expect(text()).not.toContain(' is off')
  })
})
