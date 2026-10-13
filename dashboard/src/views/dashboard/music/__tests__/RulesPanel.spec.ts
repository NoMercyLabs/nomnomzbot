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

import RulesPanel from '../components/RulesPanel.vue'
import en from '../i18n/en.json'

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown }
type Call = {
  method: string
  path: string
  blockedTrackId: unknown
  body: unknown
}
type Answer = {
  data?: unknown
  error?: unknown
  response: { status: number; ok: boolean }
}

const calls: Call[] = []
let config: Record<string, unknown> | null = null
let blocked: Record<string, unknown>[] = []

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } })

const baseConfig = (): Record<string, unknown> => ({
  isEnabled: true,
  preferredProvider: 'spotify',
  maxQueueSize: 20,
  maxRequestsPerUser: 1,
  allowYouTube: false,
  allowSpotify: true,
  minTrustLevel: 'moderators',
  bangerAutoCreate: false,
})

const blockedRow = (id: string, title: string): Record<string, unknown> => ({
  id,
  provider: 'spotify',
  trackUri: `spotify:track:${id}`,
  title,
  reason: null,
  blockedByUserId: null,
  createdAt: '2026-10-01T10:00:00Z',
})

function write(method: string, path: string, options?: Options): Answer {
  calls.push({
    method,
    path,
    blockedTrackId: options?.params?.path?.blockedTrackId,
    body: options?.body,
  })

  if (method === 'PUT') {
    config = { ...config, ...(options?.body as Record<string, unknown>) }
  }

  if (method === 'DELETE') {
    blocked = blocked.filter((row) => row.id !== options?.params?.path?.blockedTrackId)
  }

  if (method === 'POST') {
    const body = options?.body as Record<string, unknown>

    blocked = [...blocked, { ...blockedRow(String(body.trackUri), String(body.title)), ...body }]
  }

  return method === 'PUT'
    ? ok(config)
    : { response: { status: 200, ok: true } }
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) =>
      path.endsWith('/music/config')
        ? ok(config)
        : ok(blocked.map((row) => ({ ...row }))),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null
let pinia: Pinia

beforeEach(() => {
  calls.length = 0
  config = baseConfig()
  blocked = [blockedRow('b1', 'Never Gonna Give You Up'), blockedRow('b2', 'Baby Shark')]
  pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.mergeLocaleMessage('en', en)
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

const settle = async (): Promise<void> => {
  await flushPromises()
  await new Promise((done) => setTimeout(done, 0))
  await flushPromises()
}

async function open(): Promise<void> {
  mounted = mount(RulesPanel, {
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

const buttonIn = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement
const radio = (value: string): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>(
    `[data-slot="radio-group-item"][value="${value}"]`,
  ) as HTMLButtonElement
const perPerson = (): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(
    'input[name="songs-per-person"]',
  ) as HTMLInputElement
const puts = (): Call[] => calls.filter((call) => call.method === 'PUT')

describe('RulesPanel', () => {
  it('shows the saved rules, the blocked songs and what cannot be set yet', async () => {
    await open()

    expect(radio('moderators').getAttribute('aria-checked')).toBe('true')
    expect(radio('everyone').getAttribute('aria-checked')).toBe('false')
    expect(perPerson().value).toBe('1')
    const text = document.body.textContent ?? ''

    expect(text).toContain('Never Gonna Give You Up')
    expect(text).toContain('Baby Shark')
    expect(text).toContain('Not available yet')
    expect(buttonIn('Save').disabled).toBe(true)
  })

  it('saves only the fields that changed', async () => {
    await open()

    radio('subscribers').click()
    await settle()
    expect(buttonIn('Save').disabled).toBe(false)
    buttonIn('Save').click()
    await settle()

    expect(puts()).toHaveLength(1)
    expect(puts()[0]!.body).toEqual({ minTrustLevel: 'subscribers' })
    expect(buttonIn('Save').disabled).toBe(true)
  })

  it('saves the songs per person the streamer types', async () => {
    await open()

    perPerson().value = '4'
    perPerson().dispatchEvent(new Event('input'))
    await settle()
    buttonIn('Save').click()
    await settle()

    expect(puts()[0]!.body).toEqual({ maxRequestsPerUser: 4 })
  })

  it('does not save a songs per person below one', async () => {
    await open()

    perPerson().value = '0'
    perPerson().dispatchEvent(new Event('input'))
    await settle()

    expect(buttonIn('Save').disabled).toBe(true)
  })

  it('discards the edits without a request and shows the saved rule again', async () => {
    await open()

    radio('everyone').click()
    await settle()
    expect(radio('everyone').getAttribute('aria-checked')).toBe('true')
    buttonIn('Discard').click()
    await settle()

    expect(calls).toHaveLength(0)
    expect(radio('moderators').getAttribute('aria-checked')).toBe('true')
    expect(buttonIn('Save').disabled).toBe(true)
  })

  it('allows a blocked song with a DELETE and puts it back on Undo with a POST', async () => {
    await open()

    buttonIn('Allow Baby Shark').click()
    await settle()

    expect(calls).toHaveLength(1)
    expect(calls[0]!.method).toBe('DELETE')
    expect(calls[0]!.path.endsWith('/music/blocked-tracks/{blockedTrackId}')).toBe(true)
    expect(calls[0]!.blockedTrackId).toBe('b2')
    expect(document.body.querySelectorAll('[data-blocked-item]')).toHaveLength(1)

    expect(document.body.querySelector('[data-blocked-item]')?.textContent).toContain(
      'Never Gonna Give You Up',
    )

    buttonIn('Undo').click()
    await settle()

    expect(calls[1]!.method).toBe('POST')
    expect(calls[1]!.path.endsWith('/music/blocked-tracks')).toBe(true)

    expect(calls[1]!.body).toEqual({
      provider: 'spotify',
      trackUri: 'spotify:track:b2',
      title: 'Baby Shark',
    })

    expect(document.body.querySelectorAll('[data-blocked-item]')).toHaveLength(2)
  })

  it('says there are no blocked songs when the list is empty', async () => {
    blocked = []
    await open()

    expect(document.body.textContent).toContain('No songs are blocked.')
  })

  it('shows the not-available reason when the rules are missing', async () => {
    config = null
    await open()

    expect(document.body.querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Not available yet',
    )
  })
})
