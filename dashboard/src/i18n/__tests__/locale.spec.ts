// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { defineComponent } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const page = { default: defineComponent({ name: 'TestPage', render: () => null }) }
// A key of this test's own page, not of the app, so the lint key check does not apply to it.
const titleKey: string = 'page.title'
const pageWords: Record<string, { page: { title: string } }> = {
  en: { page: { title: 'Welcome' } },
  nl: { page: { title: 'Welkom' } },
}

async function freshI18n(): Promise<typeof import('@/i18n')> {
  vi.resetModules()

  return import('@/i18n')
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.lang = ''
})

// Stoney 2026-10-13 (translated from Dutch): "It must be possible to switch the language."
describe('switching the language', () => {
  it('reloads the words of a page that is already open, and the shared words', async () => {
    const { i18n, loadPage, setLocale } = await freshI18n()

    await loadPage(
      async () => page,
      async (locale: string) => ({ default: pageWords[locale]! }),
    )
    expect(i18n.global.t(titleKey)).toBe('Welcome')

    await setLocale('nl')

    expect(i18n.global.t(titleKey)).toBe('Welkom')
    expect(i18n.global.t('common.signOut')).toBe('Afmelden')
    expect(document.documentElement.lang).toBe('nl')
  })

  it('keeps the choice for the next visit', async () => {
    const first = await freshI18n()

    await first.setLocale('nl')

    const next = await freshI18n()

    await next.startLocale()

    expect(next.i18n.global.locale.value).toBe('nl')
    expect(next.i18n.global.t('common.signOut')).toBe('Afmelden')
  })

  it('gives a page that opens during the switch the new language', async () => {
    localStorage.setItem('nnz.locale', 'nl')
    const { i18n, loadPage, startLocale } = await freshI18n()

    const starting = startLocale()

    await loadPage(
      async () => page,
      async (locale: string) => ({ default: pageWords[locale]! }),
    )
    await starting

    expect(i18n.global.t(titleKey)).toBe('Welkom')
  })

  it('shows English words for a page that has no words in the chosen language yet', async () => {
    const { i18n, loadPage, setLocale } = await freshI18n()

    await setLocale('nl')
    await loadPage(
      async () => page,
      async (locale: string) => {
        if (locale !== 'en') throw new Error(`no ${locale} file`)

        return { default: pageWords.en! }
      },
    )

    expect(i18n.global.t(titleKey)).toBe('Welcome')
  })
})
