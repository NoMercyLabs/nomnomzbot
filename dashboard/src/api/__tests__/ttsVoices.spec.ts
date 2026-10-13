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
import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createApp, type App } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ttsConfigKey,
  ttsVoicesKey,
  useSetTtsVoice,
  useTtsConfig,
  useTtsSample,
  useTtsVoices,
  voiceFamily,
  type TtsConfig,
  type TtsVoice,
} from '../tts'

type Answer = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}))

vi.mock('../client', () => ({ client: http }))

const config: TtsConfig = {
  isEnabled: true,
  mode: 'client_edge',
  defaultProvider: 'edge',
  defaultVoiceId: 'en-US-AriaNeural',
  maxCharacters: 300,
  minPermission: 'everyone',
  skipBotMessages: true,
  readUsernames: false,
  profanityCensorEnabled: true,
  modApprovalRequired: false,
  minBitsToTts: null,
  viewerVoiceSelfServiceEnabled: true,
  hasAzureByokKey: false,
  hasElevenLabsByokKey: false,
  azureRegion: null,
  followsPlatformDefaultVoice: false,
}

const voice = (id: string, locale: string): TtsVoice => ({
  id,
  name: id,
  displayName: id,
  locale,
  gender: 'female',
  provider: 'edge',
  isDefault: false,
  accent: null,
  age: null,
  styles: [],
  tags: [],
  description: null,
  previewUrl: null,
})

function mount<T>(use: () => T): { queries: QueryClient; hook: T } {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  let hook!: T
  const app: App = createApp({
    setup() {
      hook = use()

      return () => null
    },
  })

  app.use(VueQueryPlugin, { queryClient: queries })
  app.mount(document.createElement('div'))

  return { queries, hook }
}

beforeEach(() => {
  setActivePinia(createPinia())
  http.GET.mockReset()
  http.PUT.mockReset()
  http.POST.mockReset()
})

describe('voiceFamily', () => {
  it('groups a voice by the language part of its locale and puts the rest under other', () => {
    expect(voiceFamily('en-US')).toBe('en')
    expect(voiceFamily('nl_BE')).toBe('nl')
    expect(voiceFamily('fr-CA')).toBe('fr')
    expect(voiceFamily('ja-JP')).toBe('other')
    expect(voiceFamily('')).toBe('other')
  })
})

describe('useTtsVoices', () => {
  // The server caps a page at 200 and sends no total, only whether more pages exist.
  it('searches the catalog with the typed words and reads the first page of 200', async () => {
    http.GET.mockResolvedValue({
      data: { data: [voice('nl-NL-ColetteNeural', 'nl-NL')], hasMore: false, nextPage: null },
      response: { ok: true, status: 200 },
    })

    const { queries, hook } = mount(() => useTtsVoices('c1', { q: 'colette' }))

    await flushPromises()

    expect(http.GET).toHaveBeenCalledWith('/api/v{version}/channels/{channelId}/tts/voices', {
      params: {
        path: { version: '1', channelId: 'c1' },
        query: { q: 'colette', page: 1, pageSize: 200 },
      },
    })

    expect(
      hook.data.value?.pages.flatMap((page) => page.voices.map((row: TtsVoice) => row.id)),
    ).toEqual(['nl-NL-ColetteNeural'])

    expect(hook.hasNextPage.value).toBe(false)
    expect(queries.getQueryData(ttsVoicesKey('c1', { q: 'colette' }))).toEqual(hook.data.value)
  })

  it('appends the next page while the server says there are more and stops after the last one', async () => {
    http.GET.mockResolvedValueOnce({
      data: { data: [voice('en-US-AriaNeural', 'en-US')], hasMore: true, nextPage: 2 },
      response: { ok: true, status: 200 },
    })

    http.GET.mockResolvedValueOnce({
      data: { data: [voice('en-GB-SoniaNeural', 'en-GB')], hasMore: false, nextPage: null },
      response: { ok: true, status: 200 },
    })

    const { hook } = mount(() => useTtsVoices('c1', { q: '' }))

    await flushPromises()
    expect(hook.hasNextPage.value).toBe(true)

    await hook.fetchNextPage()
    await flushPromises()

    expect(http.GET).toHaveBeenLastCalledWith('/api/v{version}/channels/{channelId}/tts/voices', {
      params: { path: { version: '1', channelId: 'c1' }, query: { page: 2, pageSize: 200 } },
    })

    expect(
      hook.data.value?.pages.flatMap((page) => page.voices.map((row: TtsVoice) => row.id)),
    ).toEqual(['en-US-AriaNeural', 'en-GB-SoniaNeural'])

    expect(hook.hasNextPage.value).toBe(false)
  })

  it('turns a refused read into an error that names the status', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 500 } })

    const { hook } = mount(() => useTtsVoices('c1', { q: '' }))

    await flushPromises()

    expect(hook.error.value?.message).toBe('tts voices 500')
  })
})

describe('useSetTtsVoice', () => {
  it('sends the voice and its provider, drops the platform follow, and shows the pick at once', async () => {
    let release!: (answer: Answer) => void

    http.GET.mockResolvedValue({ data: { data: config }, response: { ok: true, status: 200 } })
    http.PUT.mockReturnValue(new Promise<Answer>((done) => (release = done)))

    const { queries, hook } = mount(() => ({
      config: useTtsConfig('c1'),
      save: useSetTtsVoice('c1'),
    }))

    await flushPromises()

    hook.save.mutate({ voiceId: 'nl-NL-ColetteNeural', provider: 'edge' })
    await flushPromises()

    expect(http.PUT).toHaveBeenCalledWith('/api/v{version}/channels/{channelId}/tts/config', {
      params: { path: { version: '1', channelId: 'c1' } },
      body: {
        defaultVoiceId: 'nl-NL-ColetteNeural',
        defaultProvider: 'edge',
        followPlatformDefaultVoice: false,
      },
    })

    expect(queries.getQueryData<TtsConfig>(ttsConfigKey('c1'))?.defaultVoiceId).toBe(
      'nl-NL-ColetteNeural',
    )

    release({
      data: { data: { ...config, defaultVoiceId: 'nl-NL-ColetteNeural' } },
      response: { ok: true, status: 200 },
    })

    await flushPromises()

    expect(queries.getQueryData<TtsConfig>(ttsConfigKey('c1'))?.defaultVoiceId).toBe(
      'nl-NL-ColetteNeural',
    )
  })

  it('rolls the pick back to the old voice when the bot refuses the save', async () => {
    http.GET.mockResolvedValue({ data: { data: config }, response: { ok: true, status: 200 } })

    http.PUT.mockResolvedValue({
      error: { message: 'No such voice' },
      response: { ok: false, status: 400 },
    })

    const { queries, hook } = mount(() => ({
      config: useTtsConfig('c1'),
      save: useSetTtsVoice('c1'),
    }))

    await flushPromises()

    hook.save.mutate({ voiceId: 'nl-NL-ColetteNeural', provider: 'edge' })
    await flushPromises()

    expect(hook.save.error.value?.message).toBe('No such voice')
    expect(queries.getQueryData<TtsConfig>(ttsConfigKey('c1'))).toEqual(config)
  })
})

describe('useTtsSample', () => {
  it('asks the bot to speak a sample line in the chosen voice and returns the audio', async () => {
    http.POST.mockResolvedValue({
      data: {
        data: {
          voiceId: 'nl-NL-ColetteNeural',
          provider: 'edge',
          durationMs: 900,
          audioBase64: 'QUJD',
        },
      },
      response: { ok: true, status: 200 },
    })

    const { hook } = mount(() => useTtsSample('c1'))
    const result = await hook.mutateAsync({ voiceId: 'nl-NL-ColetteNeural', text: 'Hello chat' })

    expect(http.POST).toHaveBeenCalledWith('/api/v{version}/channels/{channelId}/tts/test', {
      params: { path: { version: '1', channelId: 'c1' } },
      body: { text: 'Hello chat', voiceId: 'nl-NL-ColetteNeural' },
    })

    expect(result.audioBase64).toBe('QUJD')
  })

  it('reports the bot reason when the sample is refused', async () => {
    http.POST.mockResolvedValue({
      error: { message: 'Provider not configured' },
      response: { ok: false, status: 422 },
    })

    const { hook } = mount(() => useTtsSample('c1'))

    await expect(hook.mutateAsync({ voiceId: 'x', text: 'Hi' })).rejects.toThrow(
      'Provider not configured',
    )
  })
})
