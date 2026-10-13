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

import { i18n } from '@/i18n'

import en from '../i18n/en.json'
import VoicesTab from '../VoicesTab.vue'

type Answer = {
  data?: unknown
  error?: unknown
  response: { status: number; ok: boolean }
}
type Call = {
  method: string
  path: string
  init: { params?: { query?: Record<string, unknown> }; body?: unknown }
}

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}

const ok = (data: unknown, extra: Record<string, unknown> = {}): Answer => ({
  data: { data, ...extra },
  response: { status: 200, ok: true },
})

const voice = (id: string, locale: string, gender: string = 'female'): Record<string, unknown> => ({
  id,
  name: id,
  displayName: id,
  locale,
  gender,
  provider: 'edge',
  isDefault: false,
  accent: null,
  age: null,
  styles: [],
  tags: [],
  description: null,
  previewUrl: null,
})

const config = {
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

const catalog: Record<string, unknown>[] = [
  voice('en-US-AriaNeural', 'en-US'),
  voice('nl-NL-ColetteNeural', 'nl-NL'),
  voice('nl-BE-ArnaudNeural', 'nl-BE', 'male'),
  voice('ja-JP-NanamiNeural', 'ja-JP'),
]

function record(method: string, path: string, init: Call['init']): Answer | undefined {
  calls.push({ method, path, init })
  const match = Object.keys(answers).find(
    (key) => key.startsWith(`${method} `) && path.endsWith(key.slice(method.length + 1)),
  )

  return match
    ? answers[match]!()
    : undefined
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, init: Call['init']) => record('GET', path, init) ?? ok(null),
    PUT: async (path: string, init: Call['init']) => record('PUT', path, init) ?? ok(null),
    POST: async (path: string, init: Call['init']) => record('POST', path, init) ?? ok(null),
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

const ROW_HEIGHT = 56
const VIEWPORT_HEIGHT = 560

let mounted: VueWrapper | null = null
let pinia: Pinia
const played: string[] = []

beforeEach(() => {
  localStorage.clear()
  calls.length = 0
  played.length = 0

  answers = {
    'GET /tts/config': () => ok(config),
    'GET /tts/voices': () => ok(catalog, { hasMore: false, nextPage: null }),
  }

  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)

  // jsdom has no layout: the virtual list gets a viewport and a row height, as in PeopleView.spec.ts.
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  )

  vi.stubGlobal(
    'Audio',
    class {
      constructor(public src: string) {}
      addEventListener(): void {}
      pause(): void {}
      async play(): Promise<void> {
        played.push(this.src)
      }
    },
  )

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? VIEWPORT_HEIGHT
      : ROW_HEIGHT

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this)
  })

  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this)
  })

  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(300)

  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 300,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 300,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect
  })
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(): Promise<void> {
  mounted = mount(VoicesTab, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })

  await settle()
}

const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-voice-row]'),
]
const ids = (): (string | null)[] => rows().map((row) => row.getAttribute('data-voice-id'))
const rowOf = (id: string): HTMLElement =>
  rows().find((row) => row.getAttribute('data-voice-id') === id) as HTMLElement
const count = (): string =>
  document.body.querySelector('[data-voice-count]')?.textContent?.trim() ?? ''
const chip = (family: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-voice-family="${family}"]`) as HTMLElement
const chipText = (family: string): string =>
  chip(family).textContent?.replace(/\s+/g, ' ').trim() ?? ''
const headings = (): string[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-voice-group]')].map(
    (heading) => heading.textContent?.trim() ?? '',
  )
const writes = (method: string): Call[] => calls.filter((call) => call.method === method)

describe('VoicesTab', () => {
  it('lists the catalog, marks the channel voice and counts the voices', async () => {
    await open()

    expect(ids()).toEqual([
      'en-US-AriaNeural',
      'nl-BE-ArnaudNeural',
      'nl-NL-ColetteNeural',
      'ja-JP-NanamiNeural',
    ])

    expect(rowOf('en-US-AriaNeural').getAttribute('aria-checked')).toBe('true')
    expect(rowOf('nl-NL-ColetteNeural').getAttribute('aria-checked')).toBe('false')
    expect(count()).toBe('4 of 4 voices')
  })

  it('keeps only the voices of the chosen language and counts them against the whole catalog', async () => {
    await open()

    chip('nl').click()
    await settle()
    expect(ids()).toEqual(['nl-BE-ArnaudNeural', 'nl-NL-ColetteNeural'])
    expect(count()).toBe('2 of 4 voices')

    chip('other').click()
    await settle()
    expect(ids()).toEqual(['ja-JP-NanamiNeural'])
  })

  it('sends the typed words to the catalog and shows what comes back', async () => {
    await open()
    answers['GET /tts/voices'] = () => ok([catalog[1]], { hasMore: false, nextPage: null })

    const input = document.body.querySelector('input[type="search"]') as HTMLInputElement

    input.value = 'colette'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await new Promise((done) => setTimeout(done, 350))
    await settle()

    const search = calls
      .filter((call) => call.method === 'GET' && call.path.endsWith('/tts/voices'))
      .at(-1)

    expect(search?.init.params?.query).toEqual({ q: 'colette', page: 1, pageSize: 200 })
    expect(ids()).toEqual(['nl-NL-ColetteNeural'])
    expect(count()).toBe('1 of 1 voices')
  })

  // The live catalog holds thousands of voices; the count never claims the loaded pages are all of them.
  it('says more voices exist while the server has more pages, and loads the next page on request', async () => {
    answers['GET /tts/voices'] = () => ok(catalog, { hasMore: true, nextPage: 2 })
    await open()

    expect(count()).toBe('4 of the first 4 voices')

    answers['GET /tts/voices'] = () =>
      ok([voice('de-DE-KatjaNeural', 'de-DE')], { hasMore: false, nextPage: null })

    document.body.querySelector<HTMLElement>('[data-voice-more]')!.click()
    await settle()

    const next = calls
      .filter((call) => call.method === 'GET' && call.path.endsWith('/tts/voices'))
      .at(-1)

    expect(next?.init.params?.query).toEqual({ page: 2, pageSize: 200 })
    expect(ids()).toContain('de-DE-KatjaNeural')
    expect(count()).toBe('5 of 5 voices')
    expect(document.body.querySelector('[data-voice-more]')).toBeNull()
  })

  it('names the voice provider in words, not the raw server id', async () => {
    i18n.global.mergeLocaleMessage('en', (await import('@/i18n/locales/en.json')).default)
    await open()

    const facts: string = document.body.querySelector('[data-slot="panel-card"]')?.textContent ?? ''

    expect(facts).toContain('Microsoft Edge')
    expect(facts).not.toMatch(/\bedge\b/)
  })

  it('saves a picked voice with the provider and shows it as the channel voice', async () => {
    answers['PUT /tts/config'] = () => ok({ ...config, defaultVoiceId: 'nl-NL-ColetteNeural' })
    await open()

    rowOf('nl-NL-ColetteNeural').click()
    await settle()

    expect(writes('PUT')).toHaveLength(1)

    expect(writes('PUT')[0]!.init.body).toEqual({
      defaultVoiceId: 'nl-NL-ColetteNeural',
      defaultProvider: 'edge',
      followPlatformDefaultVoice: false,
    })

    expect(rowOf('nl-NL-ColetteNeural').getAttribute('aria-checked')).toBe('true')
    expect(rowOf('en-US-AriaNeural').getAttribute('aria-checked')).toBe('false')
  })

  it('puts the old voice back when the bot refuses the pick', async () => {
    answers['PUT /tts/config'] = () => ({
      error: { message: 'No such voice' },
      response: { status: 400, ok: false },
    })

    await open()

    rowOf('nl-NL-ColetteNeural').click()
    await settle()

    expect(writes('PUT')).toHaveLength(1)
    expect(rowOf('en-US-AriaNeural').getAttribute('aria-checked')).toBe('true')
    expect(rowOf('nl-NL-ColetteNeural').getAttribute('aria-checked')).toBe('false')
  })

  it('sends no save when the channel voice is clicked again', async () => {
    await open()

    rowOf('en-US-AriaNeural').click()
    await settle()

    expect(writes('PUT')).toHaveLength(0)
  })

  it('asks the bot for a sample in that voice and plays the audio it returns', async () => {
    answers['POST /tts/test'] = () =>
      ok({ voiceId: 'nl-NL-ColetteNeural', provider: 'edge', durationMs: 900, audioBase64: 'QUJD' })

    await open()

    rowOf('nl-NL-ColetteNeural')
      .parentElement!.querySelector<HTMLElement>('[data-voice-play]')!
      .click()

    await settle()

    expect(writes('POST')).toHaveLength(1)

    expect(writes('POST')[0]!.init.body).toEqual({
      text: 'Hello chat, this is how I sound.',
      voiceId: 'nl-NL-ColetteNeural',
    })

    expect(played).toEqual(['data:audio/mpeg;base64,QUJD'])
    expect(writes('PUT')).toHaveLength(0)
  })

  it('groups the voices under one heading per language, English first and the United States before other English', async () => {
    answers['GET /tts/voices'] = () =>
      ok(
        [
          voice('nl-NL-ColetteNeural', 'nl-NL'),
          voice('en-GB-SoniaNeural', 'en-GB'),
          voice('ja-JP-NanamiNeural', 'ja-JP'),
          voice('en-US-AriaNeural', 'en-US'),
          voice('de-DE-KatjaNeural', 'de-DE'),
        ],
        { hasMore: false, nextPage: null },
      )

    await open()

    expect(headings()).toEqual([
      'English (United States)',
      'English (United Kingdom)',
      'Dutch (Netherlands)',
      'German (Germany)',
      'Japanese (Japan)',
    ])

    expect(ids()).toEqual([
      'en-US-AriaNeural',
      'en-GB-SoniaNeural',
      'nl-NL-ColetteNeural',
      'de-DE-KatjaNeural',
      'ja-JP-NanamiNeural',
    ])
  })

  it('shows how many voices each language chip holds', async () => {
    await open()

    expect(chipText('all')).toBe('All 4')
    expect(chipText('nl')).toBe('Dutch 2')
    expect(chipText('en')).toBe('English 1')
    expect(chip('all').getAttribute('aria-pressed')).toBe('true')
    chip('nl').click()
    await settle()
    expect(chip('nl').getAttribute('aria-pressed')).toBe('true')
    expect(chip('all').getAttribute('aria-pressed')).toBe('false')
  })

  it('remembers the chosen language after the page is left and opened again', async () => {
    await open()
    chip('nl').click()
    await settle()
    mounted?.unmount()

    await open()

    expect(chip('nl').getAttribute('aria-pressed')).toBe('true')
  })

  it('shows the catalog total only when the bot returns it', async () => {
    await open()
    expect(document.body.textContent).not.toContain('322')
    mounted!.unmount()

    answers['GET /tts/voices'] = () => ok(catalog, { hasMore: true, nextPage: 2, total: 322 })
    await open()
    expect(document.body.textContent).toContain('322 voices')
  })

  it('puts a labeled play button on the channel voice card that samples the channel voice', async () => {
    answers['POST /tts/test'] = () =>
      ok({ voiceId: 'en-US-AriaNeural', provider: 'edge', durationMs: 900, audioBase64: 'QUJD' })

    await open()

    const card = document.body.querySelector('[data-current-voice]') as HTMLElement

    expect(card.textContent).toContain('English (United States)')
    expect(card.textContent).toContain('Female')
    const play = card.querySelector<HTMLElement>('[data-voice-play]') as HTMLElement

    expect(play.textContent).toContain('Play sample')
    play.click()
    await settle()

    expect(writes('POST')[0]!.init.body).toEqual({
      text: 'Hello chat, this is how I sound.',
      voiceId: 'en-US-AriaNeural',
    })

    expect(played).toEqual(['data:audio/mpeg;base64,QUJD'])
  })

  it('shows the error state with a retry when the catalog cannot be read', async () => {
    answers['GET /tts/voices'] = () => ({
      error: { status: 'error' },
      response: { status: 500, ok: false },
    })

    await open()

    const card: HTMLElement | undefined = [
      ...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]'),
    ].find((panel) => panel.querySelector('h2')?.textContent?.includes('Voices'))

    expect(card?.getAttribute('data-state')).toBe('error')
    expect(rows()).toHaveLength(0)
  })
})
