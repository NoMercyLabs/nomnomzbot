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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'

import { i18n } from '@/i18n'

import en from '../i18n/en.json'
import PlaySampleButton from '../PlaySampleButton.vue'

type Release = () => void

let releaseSample: Release = () => undefined
const posts: unknown[] = []
const paused: string[] = []

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    POST: async (_path: string, init: { body: unknown }) => {
      posts.push(init.body)

      await new Promise<void>((resolve) => {
        releaseSample = resolve
      })

      return {
        data: { data: { voiceId: 'v1', provider: 'edge', durationMs: 900, audioBase64: 'QUJD' } },
        response: { status: 200, ok: true },
      }
    },
  },
}))

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}))

let mounted: VueWrapper | null = null

beforeEach(() => {
  posts.length = 0
  paused.length = 0
  i18n.global.mergeLocaleMessage('en', en)

  vi.stubGlobal(
    'Audio',
    class {
      constructor(public src: string) {}
      addEventListener(): void {}
      pause(): void {
        paused.push(this.src)
      }
      async play(): Promise<void> {}
    },
  )
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  vi.unstubAllGlobals()
})

function open(): void {
  mounted = mount(PlaySampleButton, {
    props: { voiceId: 'v1' },
    global: {
      plugins: [
        i18n,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  })
}

const button = (): HTMLButtonElement =>
  mounted!.get('[data-voice-play]').element as HTMLButtonElement
const state = (): string | null => button().getAttribute('data-state')
const words = (): string => button().textContent?.trim() ?? ''

describe('PlaySampleButton', () => {
  it('is idle with the words Play sample until it is pressed', () => {
    open()

    expect(state()).toBe('idle')
    expect(words()).toBe('Play sample')
    expect(button().disabled).toBe(false)
  })

  it('is disabled and busy while the bot makes the sample, then plays with a Stop label', async () => {
    open()

    button().click()
    await flushPromises()
    expect(posts).toEqual([{ text: 'Hello chat, this is how I sound.', voiceId: 'v1' }])
    expect(state()).toBe('loading')
    expect(button().disabled).toBe(true)
    expect(button().getAttribute('aria-busy')).toBe('true')

    releaseSample()
    await flushPromises()
    expect(state()).toBe('playing')
    expect(words()).toBe('Stop')
    expect(button().disabled).toBe(false)
  })

  it('stops the audio and goes back to idle when pressed while playing', async () => {
    open()
    button().click()
    await flushPromises()
    releaseSample()
    await flushPromises()

    button().click()
    await flushPromises()

    expect(paused).toEqual(['data:audio/mpeg;base64,QUJD'])
    expect(state()).toBe('idle')
    expect(posts).toHaveLength(1)
  })

  it('can hide its words and keep a name for screen readers', () => {
    mounted = mount(PlaySampleButton, {
      props: { voiceId: 'v1', compact: true },
      global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
    })

    expect(words()).toBe('')
    expect(button().getAttribute('aria-label')).toBe('Play a sample of v1')
  })

  it('stays readable without words: a 36px button with a 20px icon', () => {
    mounted = mount(PlaySampleButton, {
      props: { voiceId: 'v1', compact: true },
      global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
    })

    expect(button().classList).toContain('size-9')
    expect(button().querySelector('svg')?.classList).toContain('size-5')
  })
})
