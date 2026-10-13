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
  builtinsKey,
  commandsKey,
  useBuiltins,
  useCommands,
  useCreateCommand,
  useDeleteCommand,
  useToggleBuiltin,
  useToggleCommand,
  useUpdateCommand,
  type BuiltinCommand,
  type CommandListItem,
} from '../commands'
import { pipelinesKey, usePipelines } from '../pipelines'

type Answer = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PATCH: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}))

vi.mock('../client', () => ({ client: http }))

const ok: Answer['response'] = { ok: true, status: 200 }
const channel = 'c1'

function row(name: string, over: Partial<CommandListItem> = {}): CommandListItem {
  return {
    id: `id-${name}`,
    name,
    tier: 'template',
    minPermissionLevel: 'Everyone',
    isEnabled: true,
    prefixMode: 'Default',
    customPrefix: null,
    matchMode: 'StartsWith',
    matchPattern: null,
    cooldownSeconds: 5,
    userCooldownSeconds: 0,
    cooldownPerUser: false,
    description: null,
    aliases: [],
    useCount: 3,
    createdAt: '2026-10-01T00:00:00Z',
    templateResponse: 'hi',
    templateResponses: null,
    pipelineId: null,
    presetKey: null,
    speakWithTts: false,
    ...over,
  }
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0))

function mount<T>(use: () => T): { queries: QueryClient; hook: T; inbox: ReturnType<typeof useSaveErrorsStore> } {
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

function names(queries: QueryClient): string[] | undefined {
  return queries.getQueryData<CommandListItem[]>(commandsKey(channel))?.map((r: CommandListItem) => r.name)
}

beforeEach(() => {
  http.GET.mockReset()
  http.POST.mockReset()
  http.PUT.mockReset()
  http.PATCH.mockReset()
  http.DELETE.mockReset()
})

describe('useCommands', () => {
  it('reads every page of the list and returns the rows in order', async () => {
    http.GET.mockImplementation(async (_path: string, init: unknown): Promise<Answer> => {
      const page: number = Number((init as { params: { query: { Page: number } } }).params.query.Page)
      return page === 1
        ? { data: { data: [row('!a')], hasMore: true, nextPage: 2 }, response: ok }
        : { data: { data: [row('!b')], hasMore: false, nextPage: null }, response: ok }
    })
    const { queries } = mount(() => useCommands(channel))
    await settle()
    await settle()

    expect(names(queries)).toEqual(['!a', '!b'])
    expect(http.GET).toHaveBeenCalledTimes(2)
    expect(http.GET.mock.calls[0]?.[0]).toBe('/api/v{version}/channels/{channelId}/commands')
  })

  it('fails with the status when the list is refused, so the page can show its error state', async () => {
    http.GET.mockResolvedValue({ error: {}, response: { ok: false, status: 500 } })
    const { hook } = mount(() => useCommands(channel))
    await settle()

    expect(hook.error.value?.message).toBe('commands 500')
  })
})

describe('useUpdateCommand', () => {
  it('sends only the changed fields to the command named in the path, then holds what the bot returns', async () => {
    http.PUT.mockResolvedValue({ data: { data: {} }, response: ok })
    http.GET.mockResolvedValue({ data: { data: [row('!hello', { cooldownSeconds: 30 })], hasMore: false }, response: ok })
    const { queries, hook } = mount(() => useUpdateCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!hello'), row('!other')])

    await hook.mutateAsync({ commandName: '!hello', change: { cooldownSeconds: 30 } })

    const [path, init] = http.PUT.mock.calls[0] as [string, { params: { path: Record<string, string> }; body: unknown }]
    expect(path).toBe('/api/v{version}/channels/{channelId}/commands/{commandName}')
    expect(init.params.path).toEqual({ version: '1', channelId: channel, commandName: '!hello' })
    expect(init.body).toEqual({ cooldownSeconds: 30 })
    expect(queries.getQueryData<CommandListItem[]>(commandsKey(channel))?.[0]?.cooldownSeconds).toBe(30)
  })

  it('shows the change at once and rolls it back when the bot refuses, with the bot message on the error', async () => {
    http.PUT.mockResolvedValue({ error: { message: 'Unknown helper {nope}' }, response: { ok: false, status: 400 } })
    const { queries, hook, inbox } = mount(() => useUpdateCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!hello', { templateResponse: 'hi' })])

    const pending: Promise<unknown> = hook.mutateAsync({ commandName: '!hello', change: { templateResponse: '{nope}' } }).catch((e: Error) => e)
    await settle()
    const error = (await pending) as Error

    expect(error.message).toBe('Unknown helper {nope}')
    expect(queries.getQueryData<CommandListItem[]>(commandsKey(channel))?.[0]?.templateResponse).toBe('hi')
    expect(inbox.items).toHaveLength(1)
  })

  it('a rename shows the new name in the cached row at once', async () => {
    http.PUT.mockReturnValue(new Promise<Answer>(() => undefined))
    const { queries, hook } = mount(() => useUpdateCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!old')])

    void hook.mutate({ commandName: '!old', change: { name: '!new' } })
    await settle()

    expect(names(queries)).toEqual(['!new'])
  })
})

describe('useToggleCommand', () => {
  it('flips the row at once, sends only isEnabled, and puts it back when refused', async () => {
    http.PUT.mockResolvedValue({ error: { message: 'no' }, response: { ok: false, status: 403 } })
    const { queries, hook, inbox } = mount(() => useToggleCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!hello', { isEnabled: true })])

    hook.mutate({ commandName: '!hello', isEnabled: false })
    await settle()

    expect((http.PUT.mock.calls[0]?.[1] as { body: unknown } | undefined)?.body).toEqual({ isEnabled: false })
    expect(queries.getQueryData<CommandListItem[]>(commandsKey(channel))?.[0]?.isEnabled).toBe(true)
    expect(inbox.items).toHaveLength(1)
  })
})

describe('useDeleteCommand', () => {
  it('removes the row at once and sends DELETE with the command name', async () => {
    http.DELETE.mockResolvedValue({ response: { ok: true, status: 204 } })
    http.GET.mockResolvedValue({ data: { data: [row('!keep')], hasMore: false }, response: ok })
    const { queries, hook } = mount(() => useDeleteCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!gone'), row('!keep')])

    await hook.mutateAsync({ commandName: '!gone' })

    const [path, init] = http.DELETE.mock.calls[0] as [string, { params: { path: Record<string, string> } }]
    expect(path).toBe('/api/v{version}/channels/{channelId}/commands/{commandName}')
    expect(init.params.path.commandName).toBe('!gone')
    expect(names(queries)).toEqual(['!keep'])
  })

  it('brings the row back when the delete fails', async () => {
    http.DELETE.mockResolvedValue({ error: {}, response: { ok: false, status: 500 } })
    const { queries, hook, inbox } = mount(() => useDeleteCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!gone'), row('!keep')])

    hook.mutate({ commandName: '!gone' })
    await settle()

    expect(names(queries)).toEqual(['!gone', '!keep'])
    expect(inbox.items).toHaveLength(1)
  })
})

describe('useCreateCommand', () => {
  it('posts the new command and marks the list stale', async () => {
    http.POST.mockResolvedValue({ data: { data: { name: '!new' } }, response: { ok: true, status: 201 } })
    const { queries, hook } = mount(() => useCreateCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!a')])

    await hook.mutateAsync({ name: '!new', tier: 'template', templateResponse: 'yo' })

    expect((http.POST.mock.calls[0]?.[1] as { body: unknown } | undefined)?.body).toEqual({ name: '!new', tier: 'template', templateResponse: 'yo' })
    expect(queries.getQueryState(commandsKey(channel))?.isInvalidated).toBe(true)
  })

  it('surfaces the bot message when the name is taken, and leaves the list alone', async () => {
    http.POST.mockResolvedValue({ error: { message: 'A command named !new already exists' }, response: { ok: false, status: 409 } })
    const { queries, hook } = mount(() => useCreateCommand(channel))
    queries.setQueryData(commandsKey(channel), [row('!new')])

    await expect(hook.mutateAsync({ name: '!new', tier: 'template', templateResponse: 'yo' })).rejects.toThrow('A command named !new already exists')
    expect(queries.getQueryState(commandsKey(channel))?.isInvalidated).toBe(false)
  })
})

describe('built-in commands', () => {
  const builtin: BuiltinCommand = {
    builtinKey: 'sr',
    name: '!sr',
    isEnabled: true,
    defaultCooldownSeconds: 5,
    defaultMinPermissionLevel: 'Everyone',
    replyGroup: 'sr',
    speakWithTts: false,
    isReserved: false,
    cooldownSecondsOverride: null,
    minPermissionLevelOverride: null,
    replyOverrideCount: 0,
  }

  it('reads the list from the builtins path', async () => {
    http.GET.mockResolvedValue({ data: { data: [builtin] }, response: ok })
    const { queries } = mount(() => useBuiltins(channel))
    await settle()

    expect(queries.getQueryData<BuiltinCommand[]>(builtinsKey(channel))?.[0]?.builtinKey).toBe('sr')
    expect(http.GET.mock.calls[0]?.[0]).toBe('/api/v{version}/channels/{channelId}/builtins')
  })

  it('the toggle sends PATCH with {enabled}, flips the row at once and restores it on refusal', async () => {
    http.PATCH.mockResolvedValue({ error: { message: 'locked' }, response: { ok: false, status: 400 } })
    const { queries, hook, inbox } = mount(() => useToggleBuiltin(channel))
    queries.setQueryData(builtinsKey(channel), [builtin])

    hook.mutate({ builtinKey: 'sr', enabled: false })
    await settle()

    const [path, init] = http.PATCH.mock.calls[0] as [string, { params: { path: Record<string, string> }; body: unknown }]
    expect(path).toBe('/api/v{version}/channels/{channelId}/builtins/{builtinKey}')
    expect(init.params.path.builtinKey).toBe('sr')
    expect(init.body).toEqual({ enabled: false })
    expect(queries.getQueryData<BuiltinCommand[]>(builtinsKey(channel))?.[0]?.isEnabled).toBe(true)
    expect(inbox.items).toHaveLength(1)
  })
})

describe('usePipelines', () => {
  it('reads the channel pipelines', async () => {
    http.GET.mockResolvedValue({ data: { data: [{ id: 'p1', name: 'Shout' }], hasMore: false }, response: ok })
    const { queries } = mount(() => usePipelines(channel))
    await settle()

    expect(queries.getQueryData<{ id: string }[]>(pipelinesKey(channel))?.map((p) => p.id)).toEqual(['p1'])
    expect(http.GET.mock.calls[0]?.[0]).toBe('/api/v{version}/channels/{channelId}/pipelines')
  })
})
