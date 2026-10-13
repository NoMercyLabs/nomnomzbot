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
  useSaveWidgetSettings,
  useSetWidgetEnabled,
  useTestWidgetEvent,
  useWidgetSchema,
  useWidgets,
  widgetSchemaKey,
  widgetsKey,
  type Widget,
} from '../widgets'

type Answer = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}))

vi.mock('../client', () => ({ client: http }))

const ok = { ok: true, status: 200 } as const satisfies Answer['response']
const channel = 'c1'

function widget(id: string, extra: Partial<Widget> = {}): Widget {
  return {
    id,
    name: `Widget ${id}`,
    description: null,
    framework: 'vue',
    source: 'first_party',
    isEnabled: true,
    overlayUrl: null,
    activeVersionId: null,
    galleryItemId: null,
    settings: {},
    eventSubscriptions: [],
    lastRuntimeError: null,
    lastRanAt: null,
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    galleryUpdateAvailable: false,
    isAttached: false,
    isCustomized: false,
    ...extra,
  } as Widget
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0))

async function until(check: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 50 && !check(); attempt += 1) await settle()
}

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
})

describe('useWidgets', () => {
  it('asks for the next page while the bot says there are more and merges the pages in order', async () => {
    http.GET.mockImplementation((_path: string, init: unknown): Promise<Answer> => {
      const page = Number((init as { params: { query: { Page: number } } }).params.query.Page)

      return Promise.resolve(
        page === 1
          ? { data: { data: [widget('a'), widget('b')], hasMore: true, nextPage: 2 }, response: ok }
          : { data: { data: [widget('c')], hasMore: false, nextPage: null }, response: ok },
      )
    })
    const { hook } = mount(() => useWidgets(channel))

    await until(() => hook.data.value !== undefined)

    expect(hook.data.value?.map((row: Widget) => row.id)).toEqual(['a', 'b', 'c'])
    expect(http.GET).toHaveBeenCalledTimes(2)
    const pages = http.GET.mock.calls.map(([, init]) =>
      Number((init as { params: { query: { Page: number } } }).params.query.Page),
    )

    expect(pages).toEqual([1, 2])
  })

  it('turns a refused list into an error state instead of an empty list', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } })
    const { hook } = mount(() => useWidgets(channel))

    await until(() => hook.isError.value)

    expect(hook.isError.value).toBe(true)
    expect(hook.error.value?.message).toBe('widgets 403')
  })
})

describe('useWidgetSchema', () => {
  it('reads the schema of one widget and caches it under the schema key', async () => {
    const schema = { widgetKey: 'k', name: 'N', fields: [], eventSubscriptions: ['chat.message'] }

    http.GET.mockResolvedValue({ data: { data: schema }, response: ok })
    const { queries, hook } = mount(() => useWidgetSchema(channel, 'w1'))

    await until(() => hook.data.value !== undefined)

    const [path, init] = http.GET.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ]

    expect(path).toBe('/api/v{version}/channels/{channelId}/widgets/{widgetId}/settings-schema')
    expect(init.params.path).toEqual({ version: '1', channelId: channel, widgetId: 'w1' })
    expect(queries.getQueryData(widgetSchemaKey(channel, 'w1'))).toEqual(schema)
  })
})

describe('useSetWidgetEnabled', () => {
  it('flips the row at once and sends only isEnabled in the PUT body', async () => {
    let finish: (answer: Answer) => void = () => {}

    http.PUT.mockReturnValue(new Promise<Answer>((done) => (finish = done)))
    http.GET.mockResolvedValue({
      data: { data: [widget('a', { isEnabled: false }), widget('b')], hasMore: false },
      response: ok,
    })
    const { queries, hook } = mount(() => useSetWidgetEnabled(channel))

    queries.setQueryData(widgetsKey(channel), [widget('a'), widget('b')])

    const sent: Promise<unknown> = hook.mutateAsync({ widgetId: 'a', isEnabled: false })

    await settle()

    const during = queries.getQueryData<Widget[]>(widgetsKey(channel))

    expect(during?.map((row: Widget) => [row.id, row.isEnabled])).toEqual([
      ['a', false],
      ['b', true],
    ])
    const [path, init] = http.PUT.mock.calls[0] as [string, unknown]

    expect(path).toBe('/api/v{version}/channels/{channelId}/widgets/{widgetId}')
    expect(init).toEqual({
      params: { path: { version: '1', channelId: channel, widgetId: 'a' } },
      body: { isEnabled: false },
    })

    finish({ response: ok })
    await sent
  })

  it('puts the exact earlier list back and reports to the inbox when the bot refuses', async () => {
    http.PUT.mockResolvedValue({
      error: { status: 'error', message: 'no' },
      response: { ok: false, status: 403 },
    })
    const before: Widget[] = [widget('a'), widget('b')]
    const { queries, hook, inbox } = mount(() => useSetWidgetEnabled(channel))

    queries.setQueryData(widgetsKey(channel), before)

    await hook.mutateAsync({ widgetId: 'a', isEnabled: false }).catch(() => undefined)
    await settle()

    expect(queries.getQueryData<Widget[]>(widgetsKey(channel))).toEqual(before)
    expect(inbox.items).toHaveLength(1)
  })
})

describe('useSaveWidgetSettings', () => {
  it('sends the full settings map as the only PUT field and shows it in the row at once', async () => {
    let finish: (answer: Answer) => void = () => {}

    http.PUT.mockReturnValue(new Promise<Answer>((done) => (finish = done)))
    http.GET.mockResolvedValue({ data: { data: [], hasMore: false }, response: ok })
    const { queries, hook } = mount(() => useSaveWidgetSettings(channel))

    queries.setQueryData(widgetsKey(channel), [widget('a', { settings: { size: 1 } })])

    const sent: Promise<unknown> = hook.mutateAsync({
      widgetId: 'a',
      settings: { size: 2, color: '#fff' },
    })

    await settle()

    expect(queries.getQueryData<Widget[]>(widgetsKey(channel))?.[0]?.settings).toEqual({
      size: 2,
      color: '#fff',
    })
    const [, init] = http.PUT.mock.calls[0] as [string, unknown]

    expect(init).toEqual({
      params: { path: { version: '1', channelId: channel, widgetId: 'a' } },
      body: { settings: { size: 2, color: '#fff' } },
    })

    finish({ response: ok })
    await sent
  })

  it('restores the saved settings and reports to the inbox when the save fails', async () => {
    http.PUT.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 500 } })
    const before: Widget[] = [widget('a', { settings: { size: 1 } })]
    const { queries, hook, inbox } = mount(() => useSaveWidgetSettings(channel))

    queries.setQueryData(widgetsKey(channel), before)

    await hook.mutateAsync({ widgetId: 'a', settings: { size: 9 } }).catch(() => undefined)
    await settle()

    expect(queries.getQueryData<Widget[]>(widgetsKey(channel))).toEqual(before)
    expect(inbox.items).toHaveLength(1)
  })
})

describe('useTestWidgetEvent', () => {
  it('sends the event type without data and resolves to the text the bot answers', async () => {
    http.POST.mockResolvedValue({ data: { data: 'Sent to 1 overlay' }, response: ok })
    const { hook } = mount(() => useTestWidgetEvent(channel))

    const result: string = await hook.mutateAsync({ eventType: 'channel.follow' })

    expect(result).toBe('Sent to 1 overlay')
    const [path, init] = http.POST.mock.calls[0] as [string, { body: Record<string, unknown> }]

    expect(path).toBe('/api/v{version}/channels/{channelId}/widgets/test-event')
    expect(init.body).toEqual({ eventType: 'channel.follow' })
    expect('data' in init.body).toBe(false)
  })

  it('surfaces a 400 as an error carrying the bot reason', async () => {
    http.POST.mockResolvedValue({
      error: { status: 'error', message: 'eventType is required' },
      response: { ok: false, status: 400 },
    })
    const { hook } = mount(() => useTestWidgetEvent(channel))

    await expect(hook.mutateAsync({ eventType: '' })).rejects.toThrow('eventType is required')
  })
})
