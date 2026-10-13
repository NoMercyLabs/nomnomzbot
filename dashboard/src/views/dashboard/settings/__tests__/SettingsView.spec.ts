// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope, nextTick, type EffectScope } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { i18n } from '@/i18n'
import {
  readabilityDefaults,
  readabilityPresets,
  readabilityStorageKey,
  useReadability,
  type Readability,
} from '@/composables/useReadability'

import en from '../i18n/en.json'
import SettingsView from '../SettingsView.vue'

let mounted: VueWrapper | null = null
let router: Router

beforeEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('style')
  for (const name of document.documentElement.getAttributeNames()) {
    if (name.startsWith('data-rd-') || name === 'data-density')
      document.documentElement.removeAttribute(name)
  }
  i18n.global.mergeLocaleMessage('en', en)
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  )
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

async function open(query: string = ''): Promise<void> {
  const blank = { template: '<div />' }
  router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/settings', name: 'dashboard-settings', component: blank }],
  })
  await router.push(`/app/settings${query}`)
  mounted = mount(SettingsView, { attachTo: document.body, global: { plugins: [i18n, router] } })
  await flushPromises()
}

const stored = (): Readability =>
  JSON.parse(localStorage.getItem(readabilityStorageKey) ?? '{}') as Readability
const html = (): HTMLElement => document.documentElement
const button = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (item: HTMLButtonElement) => item.textContent?.trim() === label,
  ) as HTMLButtonElement
const group = (label: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[aria-label="${label}"]`) as HTMLElement
const choose = async (groupLabel: string, optionLabel: string): Promise<void> => {
  const option: HTMLButtonElement = [
    ...group(groupLabel).querySelectorAll<HTMLButtonElement>('button'),
  ].find((item: HTMLButtonElement) => item.textContent?.trim() === optionLabel) as HTMLButtonElement
  option.click()
  await flushPromises()
}

describe('Readability presets', () => {
  it('the dyslexia-friendly preset sets every option, stores it and applies it to the page', async () => {
    await open()
    button('Dyslexia-friendly').click()
    await flushPromises()

    expect(stored()).toEqual(readabilityPresets.dyslexia)
    expect(Object.keys(readabilityPresets.dyslexia).sort()).toEqual(
      Object.keys(readabilityDefaults).sort(),
    )
    expect(html().dataset.rdFont).toBe('lexend')
    expect(html().dataset.rdLh).toBe('wide')
    expect(html().dataset.rdSpacing).toBe('wide')
    expect(html().dataset.rdMotion).toBe('reduce')
    expect(html().dataset.density).toBe('comfortable')
    expect(html().style.getPropertyValue('--text-sm')).toBe('16px')
  })

  it('the high contrast preset turns the contrast attribute on and Default turns every option back', async () => {
    await open()
    button('High contrast').click()
    await flushPromises()
    expect(stored().contrast).toBe(true)
    expect(html().dataset.rdContrast).toBe('on')

    button('Default').click()
    await flushPromises()
    expect(stored()).toEqual(readabilityDefaults)
    expect(html().dataset.rdContrast).toBeUndefined()
    expect(html().dataset.density).toBe('compact')
  })
})

describe('Readability options', () => {
  it('a changed option is stored and applied to the document', async () => {
    await open()
    await choose('Line spacing', 'Wider')
    await choose('Font', 'Legible')

    expect(stored().lh).toBe('wider')
    expect(stored().font).toBe('hyperlegible')
    expect(html().dataset.rdLh).toBe('wider')
    expect(html().dataset.rdFont).toBe('hyperlegible')
    expect(html().style.getPropertyValue('--lh-sm')).toBe(`${Math.round(20 * 1.3)}px`)
  })

  it('shows which option of each choice is on', async () => {
    await open()
    await choose('Line spacing', 'Wider')

    const states: (string | undefined)[] = [
      ...group('Line spacing').querySelectorAll<HTMLElement>('button'),
    ].map((item: HTMLElement) => item.dataset.state)
    expect(states).toEqual(['off', 'off', 'on'])
  })

  // The sample is a made-up chat line: a real viewer's name is channel data and never ships in the words file.
  it('shows a sample chat line with a neutral name, a space before the time, and no real viewer', async () => {
    await open()
    const sample: string = (
      document.body.querySelector('[data-slot="readability-sample"]')?.textContent ?? ''
    )
      .replace(/\s+/g, ' ')
      .trim()

    expect(sample.startsWith('A viewer 18:26 ')).toBe(true)
    expect(sample).not.toContain('DukaSoft')
  })

  it('a text size scales every text token', async () => {
    const scope: EffectScope = effectScope()
    const reading = scope.run(() => useReadability())!
    reading.settings.value = { ...reading.settings.value, size: 18 }
    await nextTick()

    expect(html().style.getPropertyValue('--text-sm')).toBe('18px')
    expect(html().style.getPropertyValue('--text-xs')).toBe('15.43px')
    scope.stop()
  })

  it('a reload keeps the stored options and applies them again', () => {
    localStorage.setItem(
      readabilityStorageKey,
      JSON.stringify({ ...readabilityDefaults, font: 'lexend', size: 17, contrast: true }),
    )
    const scope: EffectScope = effectScope()
    const reading = scope.run(() => useReadability())!

    expect(reading.settings.value.font).toBe('lexend')
    expect(reading.settings.value.size).toBe(17)
    expect(html().dataset.rdFont).toBe('lexend')
    expect(html().dataset.rdContrast).toBe('on')
    scope.stop()
  })

  it('says the options are saved on this device, never on the account', async () => {
    await open()
    expect(document.body.textContent).toContain('saved on this device')
    expect(document.body.textContent?.toLowerCase()).not.toContain('account')
  })
})

describe('This device tab', () => {
  it('opens the Readability view from its one link', async () => {
    await open('?view=device')
    expect(document.body.querySelector('[data-view="readability"]')).toBeNull()

    button('Open readability').click()
    await flushPromises()

    expect(router.currentRoute.value.query.view).toBe('channel')
    expect(document.body.querySelector('[data-view="readability"]')).not.toBeNull()
  })
})
