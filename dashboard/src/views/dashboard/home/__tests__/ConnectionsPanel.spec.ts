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

import ConnectionsPanel from '../components/ConnectionsPanel.vue'
import en from '../i18n/en.json'

type Call = {
  method: string
  path: string
  body?: unknown
}
type Answer = {
  data?: unknown
  error?: unknown
  response: { status: number; ok: boolean }
}

const calls: Call[] = []
let answers: Record<string, () => Answer> = {}

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })

const integration = (
  provider: string,
  connected: boolean,
  accountName: string | null,
): Record<string, unknown> => ({
  provider,
  connected,
  accountName,
  grantedScopeSets: [],
  capabilities: {},
  needsReauth: false,
  loginOnly: false,
  decryptFailed: false,
})

function freshAnswers(): Record<string, () => Answer> {
  return {
    '/integrations/status': () =>
      ok([
        integration('twitch', true, 'Stoney_Eagle'),
        integration('kick', false, null),
        integration('youtube', false, null),
        integration('spotify', true, 'stoney'),
        integration('discord', false, null),
      ]),
    '/bot/status': () => ok({ connected: true, displayName: 'NoMercyBot_' }),
    '/stats': () => ok({ isLive: true, platformsLive: ['twitch'] }),
  }
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      calls.push({ method: 'GET', path })
      const match = Object.keys(answers).find((suffix) => path.endsWith(suffix))

      return match
        ? answers[match]!()
        : ok(null)
    },
    POST: async (path: string, options: { body?: unknown }) => {
      calls.push({ method: 'POST', path, body: options.body })

      return ok({ authorizeUrl: 'https://id.kick.com/oauth/authorize?x=1' })
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: ref({ login: 'stoney_eagle', displayName: 'Stoney_Eagle', isLive: true }),
  }),
}))

const assign = vi.fn<(url: string) => void>()

let mounted: VueWrapper | null = null
let pinia: Pinia

beforeEach(() => {
  calls.length = 0
  answers = freshAnswers()
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

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(): Promise<void> {
  mounted = mount(ConnectionsPanel, {
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

const group = (name: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-connections-group="${name}"]`) as HTMLElement
const row = (name: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-connection="${name}"]`) as HTMLElement
const buttonIn = (root: HTMLElement, label: string): HTMLElement =>
  [...root.querySelectorAll<HTMLElement>('button')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLElement

describe('ConnectionsPanel', () => {
  it('lists Twitch with its live line, then Kick and YouTube, in the platforms group', async () => {
    await open()

    const platforms = group('platforms')

    expect(platforms.querySelectorAll('[data-connection]')).toHaveLength(3)
    expect(row('twitch').textContent).toContain('Stoney_Eagle · Live')
    expect(platforms.contains(row('kick'))).toBe(true)
    expect(platforms.contains(row('youtube'))).toBe(true)
  })

  it('says Offline for Twitch when the stream is not live', async () => {
    answers['/stats'] = () => ok({ isLive: false, platformsLive: [] })
    await open()

    expect(row('twitch').textContent).toContain('Stoney_Eagle · Offline')
    expect(row('twitch').textContent).not.toContain('Live')
  })

  it('takes each platform live state from its own entry, not from the Twitch stream', async () => {
    answers['/integrations/status'] = () =>
      ok([integration('twitch', true, 'Stoney_Eagle'), integration('youtube', true, 'NoMercy')])

    await open()

    expect(row('twitch').textContent).toContain('Stoney_Eagle · Live')
    expect(row('youtube').textContent).toContain('NoMercy · Offline')
  })

  it('shows Not connected with an outline Connect button for a platform that is not connected', async () => {
    await open()

    expect(row('kick').textContent).toContain('Not connected')
    expect(buttonIn(row('kick'), 'Connect').dataset.variant).toBe('outline')
    expect(buttonIn(row('youtube'), 'Connect')).toBeTruthy()
    expect(buttonIn(row('twitch'), 'Connect')).toBeUndefined()
  })

  it('puts the bot and the services under Bot and services', async () => {
    await open()

    const services = group('services')

    expect(document.body.textContent).toContain('Bot and services')
    expect(services.contains(row('bot'))).toBe(true)
    expect(row('bot').textContent).toContain('NoMercyBot_')
    expect(services.contains(row('spotify'))).toBe(true)
    expect(services.contains(row('discord'))).toBe(true)
    expect(services.querySelector('[data-connection="twitch"]')).toBeNull()
    expect(services.querySelector('[data-connection="kick"]')).toBeNull()
  })

  it('starts the connect flow for Kick and sends the browser to the page the bot answers', async () => {
    await open()

    buttonIn(row('kick'), 'Connect').click()
    await settle()
    document.body.querySelector<HTMLElement>('[data-flow-act="ok"]')?.click()
    await settle()

    const post = calls.find((call) => call.method === 'POST')

    expect(post?.path).toContain('/integrations/{provider}/connect')
    expect(post?.body).toMatchObject({ scopeSetKey: 'kick.chat' })
    expect(assign).toHaveBeenCalledWith('https://id.kick.com/oauth/authorize?x=1')
  })

  it('offers no Connect button for a service that has no web connect flow', async () => {
    await open()

    expect(row('discord').textContent).toContain('Not connected')
    expect(buttonIn(row('discord'), 'Connect')).toBeUndefined()
  })
})
