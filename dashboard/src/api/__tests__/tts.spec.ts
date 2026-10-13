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
import { createApp, type App } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ttsConfigKey,
  ttsOverlayKey,
  useTtsConfig,
  useTtsOverlay,
  type TtsConfig,
  type TtsOverlay,
} from '../tts'

type Answer = { data?: unknown; error?: unknown; response: { ok: boolean; status: number } }

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}))

vi.mock('../client', () => ({ client: http }))

const config: TtsConfig = {
  isEnabled: true,
  mode: 'all',
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

function mount<T>(
  channelId: string | null,
  use: (id: string | null) => T,
): { queries: QueryClient; hook: T } {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  let hook!: T
  const app: App = createApp({
    setup() {
      hook = use(channelId)

      return () => null
    },
  })

  app.use(VueQueryPlugin, { queryClient: queries })
  app.mount(document.createElement('div'))

  return { queries, hook }
}

beforeEach(() => {
  http.GET.mockReset()
})

describe('useTtsConfig', () => {
  it('reads the channel config from its own path, unwraps data and caches it under the config key', async () => {
    http.GET.mockResolvedValue({ data: { data: config }, response: { ok: true, status: 200 } })

    const { queries, hook } = mount('c1', useTtsConfig)

    await flushPromises()

    expect(http.GET).toHaveBeenCalledWith('/api/v{version}/channels/{channelId}/tts/config', {
      params: { path: { version: '1', channelId: 'c1' } },
    })

    expect(hook.data.value).toEqual(config)
    expect(queries.getQueryData(ttsConfigKey('c1'))).toEqual(config)
  })

  it('turns a refused read into an error that names the status', async () => {
    http.GET.mockResolvedValue({ error: { status: 'error' }, response: { ok: false, status: 403 } })

    const { hook } = mount('c1', useTtsConfig)

    await flushPromises()

    expect(hook.isError.value).toBe(true)
    expect(hook.error.value?.message).toBe('tts config 403')
  })

  it('waits for a channel before it asks the server', async () => {
    const { hook } = mount(null, useTtsConfig)

    await flushPromises()

    expect(http.GET).not.toHaveBeenCalled()
    expect(hook.fetchStatus.value).toBe('idle')
  })
})

describe('useTtsOverlay', () => {
  it('keeps the last run time null when the overlay never ran', async () => {
    const overlay = {
      overlayUrl: 'https://bot.test/overlay/tts/abc',
      lastRanAt: null,
    } as const satisfies TtsOverlay

    http.GET.mockResolvedValue({ data: { data: overlay }, response: { ok: true, status: 200 } })

    const { queries, hook } = mount('c1', useTtsOverlay)

    await flushPromises()

    expect(http.GET).toHaveBeenCalledWith('/api/v{version}/channels/{channelId}/tts/overlay', {
      params: { path: { version: '1', channelId: 'c1' } },
    })

    expect(hook.data.value).toEqual(overlay)
    expect(queries.getQueryData(ttsOverlayKey('c1'))).toEqual(overlay)
  })
})
