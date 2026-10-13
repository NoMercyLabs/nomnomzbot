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
import { createPinia, setActivePinia } from 'pinia'
import { createApp, type App } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useSaveErrorsStore } from '@/stores/saveErrors'

import {
  blockedTracksKey,
  musicConfigKey,
  musicQueueKey,
  useMusicQueue,
  useRemoveQueueItem,
  useSaveMusicConfig,
  type MusicConfig,
  type MusicQueue,
} from '../music'

type Answer = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}))

vi.mock('../client', () =>
  ({ client: http }))

const ok = { ok: true, status: 200 } as const satisfies Answer['response']
const channel = 'c1'

const queue: MusicQueue = {
  nowPlaying: null,
  queue: [
    {
      position: 1,
      trackName: 'One',
      artist: 'A',
      imageUrl: null,
      durationMs: 1000,
      requestedBy: 'x',
      cost: 0,
    },
    {
      position: 2,
      trackName: 'Two',
      artist: 'B',
      imageUrl: null,
      durationMs: 1000,
      requestedBy: 'y',
      cost: 0,
    },
    {
      position: 3,
      trackName: 'Three',
      artist: 'C',
      imageUrl: null,
      durationMs: 1000,
      requestedBy: 'z',
      cost: 0,
    },
  ],
}

const config: MusicConfig = {
  isEnabled: true,
  preferredProvider: 'spotify',
  maxQueueSize: 50,
  maxRequestsPerUser: 3,
  allowYouTube: false,
  allowSpotify: true,
  minTrustLevel: 'everyone',
  bangerAutoCreate: false,
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0))

function mount<T>(use: () => T): {
  queries: QueryClient
  hook: T
  inbox: ReturnType<typeof useSaveErrorsStore>
} {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const pinia = createPinia()

  setActivePinia(pinia)
  let hook!: T
  const app: App = createApp({
    setup() {
      hook = use()

      return () => null
    },
  })

  app.use(pinia)
  app.use(VueQueryPlugin, { queryClient: queries })
  app.mount(document.createElement('div'))

  return { queries, hook, inbox: useSaveErrorsStore() }
}

beforeEach(() => {
  http.GET.mockReset()
  http.POST.mockReset()
  http.PUT.mockReset()
  http.DELETE.mockReset()
})

describe('useRemoveQueueItem', () => {
  it('drops the row at once, renumbers the rest, and sends DELETE with the row position', async () => {
    let finish: (answer: Answer) => void = () => {}

    http.DELETE.mockReturnValue(new Promise<Answer>((done) => (finish = done)))
    http.GET.mockResolvedValue({
      data: {
        data: { nowPlaying: null, queue: [queue.queue[0], { ...queue.queue[2], position: 2 }] },
      },
      response: ok,
    })
    const { queries, hook } = mount(() => useRemoveQueueItem(channel))

    queries.setQueryData(musicQueueKey(channel), queue)

    const sent: Promise<unknown> = hook.mutateAsync({ position: 2 })

    await settle()

    const during = queries.getQueryData<MusicQueue>(musicQueueKey(channel))

    expect(during?.queue.map((item) => [item.position, item.trackName])).toEqual([
      [1, 'One'],
      [2, 'Three'],
    ])
    const [path, init] = http.DELETE.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ]

    expect(path).toBe('/api/v{version}/channels/{channelId}/music/queue/{position}')
    expect(init.params.path).toEqual({ version: '1', channelId: channel, position: 2 })

    finish({ response: ok })
    await sent
  })

  it('puts the exact earlier queue back and reports to the inbox when the bot refuses', async () => {
    http.DELETE.mockResolvedValue({
      error: { status: 'error', message: 'no' },
      response: { ok: false, status: 403 },
    })
    const { queries, hook, inbox } = mount(() => useRemoveQueueItem(channel))

    queries.setQueryData(musicQueueKey(channel), queue)

    await hook.mutateAsync({ position: 2 }).catch(() => undefined)
    await settle()

    expect(queries.getQueryData<MusicQueue>(musicQueueKey(channel))).toEqual(queue)
    expect(inbox.items).toHaveLength(1)
  })
})

describe('useSaveMusicConfig', () => {
  it('sends only the changed fields in the PUT body and caches the config the bot returns', async () => {
    const saved: MusicConfig = { ...config, maxRequestsPerUser: 1 }

    http.PUT.mockResolvedValue({ data: { data: saved }, response: ok })
    const { queries, hook } = mount(() => useSaveMusicConfig(channel))

    queries.setQueryData(musicConfigKey(channel), config)

    await hook.mutateAsync({ maxRequestsPerUser: 1 })

    expect(http.PUT).toHaveBeenCalledTimes(1)
    const [path, init] = http.PUT.mock.calls[0] as [string, unknown]

    expect(path).toBe('/api/v{version}/channels/{channelId}/music/config')
    expect(init).toEqual({
      params: { path: { version: '1', channelId: channel } },
      body: { maxRequestsPerUser: 1 },
    })
    expect(queries.getQueryData<MusicConfig>(musicConfigKey(channel))).toEqual(saved)
  })

  it('restores the earlier config when the PUT is refused', async () => {
    http.PUT.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 400 } })
    const { queries, hook } = mount(() => useSaveMusicConfig(channel))

    queries.setQueryData(musicConfigKey(channel), config)

    await hook.mutateAsync({ isEnabled: false }).catch(() => undefined)
    await settle()

    expect(queries.getQueryData<MusicConfig>(musicConfigKey(channel))).toEqual(config)
  })
})

describe('reads', () => {
  it('a refused queue GET is an error, not an empty queue', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } })
    const { hook } = mount(() => useMusicQueue(channel))

    await vi.waitFor(() => expect(hook.isError.value).toBe(true))

    expect(hook.data.value).toBeUndefined()
    expect(hook.error.value?.message).toBe('music queue 403')
  })

  it('keys are per channel so one streamer never reads another cache entry', () => {
    expect(musicQueueKey('a')).not.toEqual(musicQueueKey('b'))
    expect(blockedTracksKey('a')).not.toEqual(musicConfigKey('a'))
  })
})
