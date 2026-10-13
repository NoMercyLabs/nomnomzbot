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

import en from '../i18n/en.json'
import TtsView from '../TtsView.vue'

type Answer = {
  data?: unknown
  error?: unknown
  response: { status: number; ok: boolean }
}

const calls: string[] = []
let answers: Record<string, () => Answer> = {}

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })
const refused = (): Answer => ({ error: { status: 'error' }, response: { status: 403, ok: false } })

const config = {
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

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push(path)
      const match = Object.keys(answers).find((suffix) => path.endsWith(suffix))

      return match
        ? answers[match]!()
        : ok(null)
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

  answers = {
    '/tts/config': () => ok(config),
    '/tts/overlay': () => ok({ overlayUrl: 'https://bot.test/overlay/tts/abc', lastRanAt: null }),
  }

  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function open(query: string = ''): Promise<void> {
  const blank = { template: '<div />' }
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/tts', name: 'dashboard-tts', component: blank }],
  })

  await router.push(`/app/tts${query}`)

  mounted = mount(TtsView, {
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

  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

const text = (): string => document.body.textContent ?? ''
const panel = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]')].find((card) =>
    card.querySelector('h2')?.textContent?.includes(title),
  ) as HTMLElement
const rowOf = (title: string): HTMLElement =>
  [...panel('Settings').querySelectorAll<HTMLElement>('[data-settings-row]')].find((row) =>
    row.textContent?.includes(title),
  ) as HTMLElement
const badgeTone = (): string | null =>
  document.body.querySelector('[data-slot="status-badge"]')?.getAttribute('data-tone') ?? null

describe('TtsView overview', () => {
  it('shows the three tabs and starts on Overview', async () => {
    await open()

    const tabs: string[] = [...document.body.querySelectorAll('[role="tab"]')].map(
      (tab) => tab.textContent?.trim() ?? '',
    )

    expect(tabs).toEqual(['Overview', 'Voices', 'Rules'])
    expect(panel('Spoken history')).toBeDefined()
  })

  it('shows an on badge when text to speech is enabled and an off badge when it is not', async () => {
    await open()
    expect(badgeTone()).toBe('ok')
    expect(text()).toContain('Text to speech is on')

    mounted?.unmount()
    document.body.innerHTML = ''
    answers['/tts/config'] = () => ok({ ...config, isEnabled: false })
    await open()
    expect(badgeTone()).toBe('off')
    expect(text()).toContain('Text to speech is off')
  })

  it('builds the settings rows from the config: voice, who may use it, limits, words', async () => {
    await open()

    expect(rowOf('Voice').textContent).toContain('en-US-AriaNeural')
    expect(rowOf('Voice').textContent).toContain('Microsoft Edge')
    expect(rowOf('Who may use it').textContent).toContain('everyone')
    expect(rowOf('Message limits').textContent).toContain('300 characters')
    expect(rowOf('Message limits').textContent).toContain('Bot messages are skipped')
    expect(rowOf('Words and pronunciation').textContent).toContain('Profanity is censored')
  })

  it('says the channel follows the platform voice when no voice is picked', async () => {
    answers['/tts/config'] = () =>
      ok({ ...config, defaultVoiceId: null, followsPlatformDefaultVoice: true })

    await open()

    expect(rowOf('Voice').textContent).toContain('Follows the platform voice')
  })

  it('shows "Never used" for an overlay that never ran and the time when it did', async () => {
    await open()
    expect(rowOf('Speaker overlay').textContent).toContain('Never used')

    mounted?.unmount()
    document.body.innerHTML = ''

    answers['/tts/overlay'] = () =>
      ok({ overlayUrl: 'https://bot.test/overlay/tts/abc', lastRanAt: '2026-10-13T09:00:00Z' })

    await open()
    expect(rowOf('Speaker overlay').textContent).toContain('Last used')
    expect(rowOf('Speaker overlay').textContent).not.toContain('Never used')
  })

  it('gives the empty spoken history its reason instead of a made-up list', async () => {
    await open()

    expect(panel('Spoken history').getAttribute('data-state')).toBe('empty')

    expect(panel('Spoken history').textContent).toContain(
      'The bot does not keep a readable record of spoken messages yet.',
    )
  })

  it('shows the error state when the config read is refused, and no badge', async () => {
    answers['/tts/config'] = () => refused()
    await open()

    expect(panel('Settings').getAttribute('data-state')).toBe('error')
    expect(badgeTone()).toBeNull()
  })

  it('opens Rules with an honest empty panel until it is built', async () => {
    await open('?view=rules')
    expect(panel('Rules').getAttribute('data-state')).toBe('empty')
  })
})
