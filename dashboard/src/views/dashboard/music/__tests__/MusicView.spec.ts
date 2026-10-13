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
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { vi } from 'vitest'
import { ref } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'

import MusicView from '../MusicView.vue'
import en from '../i18n/en.json'

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown }
type Call = { method: string; path: string; position: unknown }
type Answer = { data?: unknown; error?: unknown; response: { status: number; ok: boolean } }

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}
let queue: Record<string, unknown>[] = []
let nowPlaying: Record<string, unknown> | null = null

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (): Answer => ({ error: { status: 'error' }, response: { status: 403, ok: false } })

const track = (
  position: number,
  trackName: string,
  artist: string,
  requestedBy: string,
): Record<string, unknown> => ({
  position,
  trackName,
  artist,
  imageUrl: null,
  durationMs: 200000,
  requestedBy,
  cost: 0,
})

const playing = (overrides: Record<string, unknown> = {}): Record<string, unknown> => ({
  trackName: 'Midnight City',
  artist: 'M83',
  album: 'Hurry Up',
  imageUrl: null,
  durationMs: 240000,
  progressMs: 60000,
  isPlaying: true,
  volume: 40,
  requestedBy: 'DukaSoft',
  provider: 'spotify',
  shuffleState: false,
  repeatState: 'off',
  canSetShuffle: true,
  canSetRepeat: true,
  canSkipNext: true,
  canSkipPrevious: true,
  canSeek: true,
  canPause: true,
  canResume: true,
  ...overrides,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/music/queue': () => ok({ nowPlaying, queue: queue.map((item) => ({ ...item })) }),
    '/integrations/status': () =>
      ok([
        {
          provider: 'spotify',
          connected: true,
          accountName: 'stoney',
          needsReauth: false,
          grantedScopeSets: [],
          capabilities: {},
          loginOnly: false,
        },
      ]),
  }
}

function read(path: string): Answer {
  const match = Object.keys(answers).find((suffix) => path.endsWith(suffix))

  return match
    ? answers[match]!()
    : ok(null)
}

function write(method: string, path: string, options?: Options): Answer {
  calls.push({ method, path, position: options?.params?.path?.position })

  if (method === 'DELETE') {
    queue = queue.filter((item) => item.position !== options?.params?.path?.position)
  }

  return { response: { status: 200, ok: true } }
}

vi.mock('@/api/client', () =>
  ({
    client: {
      use: () => undefined,
      GET: async (path: string) => read(path),
      POST: async (path: string, options?: Options) => write('POST', path, options),
      DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
    },
  }))

vi.mock('@/composables/useCurrentChannel', () =>
  ({
    useCurrentChannel: () => ({ channelId: ref('c1') }),
  }))

let mounted: VueWrapper | null = null
let pinia: Pinia

beforeEach(() => {
  calls.length = 0
  queue = [
    track(1, 'Strobe', 'deadmau5', 'sample_viewer'),
    track(2, 'Opus', 'Eric Prydz', 'DukaSoft'),
  ]
  nowPlaying = playing()
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
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/app/music', name: 'dashboard-music', component: blank },
      { path: '/app/rewards', name: 'dashboard-rewards', component: blank },
    ],
  })

  await router.push('/app/music')
  mounted = mount(MusicView, {
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

const panel = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]')].find((card) =>
    card.querySelector('h2')?.textContent?.includes(title),
  ) as HTMLElement
const buttonIn = (root: HTMLElement, label: string): HTMLButtonElement =>
  [...root.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement
const writes = (): Call[] => calls

describe('MusicView', () => {
  it('shows the playing track and the queue rows from the queue answer', async () => {
    await open()

    const now = panel('Now playing').textContent ?? ''

    expect(now).toContain('Midnight City')
    expect(now).toContain('M83')
    expect(now).toContain('DukaSoft')
    const rows = panel('Request queue').textContent ?? ''

    expect(rows).toContain('Strobe')
    expect(rows).toContain('deadmau5')
    expect(rows).toContain('sample_viewer')
    expect(rows).toContain('Opus')
    expect(document.body.textContent).toContain('Spotify connected')
  })

  it('skips with a POST to the skip path', async () => {
    await open()

    buttonIn(panel('Now playing'), 'Skip').click()
    await settle()

    expect(writes()).toHaveLength(1)
    expect(writes()[0]!.method).toBe('POST')
    expect(writes()[0]!.path.endsWith('/music/skip')).toBe(true)
  })

  it('pauses a playing track and offers Play for a paused one', async () => {
    await open()

    buttonIn(panel('Now playing'), 'Pause').click()
    await settle()

    expect(writes()[0]!.path.endsWith('/music/pause')).toBe(true)
    nowPlaying = playing({ isPlaying: false })
    mounted?.unmount()
    await open()
    calls.length = 0
    buttonIn(panel('Now playing'), 'Play').click()
    await settle()

    expect(writes()[0]!.path.endsWith('/music/resume')).toBe(true)
  })

  it('disables the controls the player says it cannot do', async () => {
    nowPlaying = playing({ canSkipNext: false, canPause: false })
    await open()

    expect(buttonIn(panel('Now playing'), 'Skip').disabled).toBe(true)
    expect(buttonIn(panel('Now playing'), 'Pause').disabled).toBe(true)
  })

  it('removes a row only after the confirm, sends its position, and the row leaves', async () => {
    await open()

    buttonIn(panel('Request queue'), 'Remove Opus').click()
    await settle()
    expect(writes()).toHaveLength(0)

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click()
    await settle()

    expect(writes()).toHaveLength(1)
    expect(writes()[0]!.method).toBe('DELETE')
    expect(writes()[0]!.path.endsWith('/music/queue/{position}')).toBe(true)
    expect(writes()[0]!.position).toBe(2)
    expect(panel('Request queue').textContent).not.toContain('Opus')
    expect(panel('Request queue').textContent).toContain('Strobe')
  })

  it('moves a song up with a POST to promote and its position', async () => {
    await open()

    buttonIn(panel('Request queue'), 'Play next Opus').click()
    await settle()

    expect(writes()[0]!.path.endsWith('/music/queue/{position}/promote')).toBe(true)
    expect(writes()[0]!.position).toBe(2)
  })

  it('says nothing is playing when the server leaves the track out', async () => {
    answers['/music/queue'] = () => ok({ queue: [] })
    await open()

    expect(panel('Now playing').querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Nothing is playing right now.',
    )
  })

  it('says the queue could not load when the server refuses it', async () => {
    answers['/music/queue'] = refused
    await open()

    expect(
      panel('Request queue').querySelector('[data-slot="panel-error"]')?.textContent,
    ).toContain('The queue could not load.')
  })

  it('says why history and thanks are empty instead of showing made-up rows', async () => {
    await open()

    expect(panel('History').querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Not recorded yet',
    )
    expect(
      panel('Thanks for the music').querySelector('[data-slot="panel-empty"]')?.textContent,
    ).toContain('Not recorded yet')
    expect(
      panel('Request rules').querySelector('[data-slot="panel-empty"]')?.textContent,
    ).toContain('Not available yet')
  })
})
