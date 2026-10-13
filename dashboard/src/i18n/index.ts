// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage, type RemovableRef } from '@vueuse/core'
import type { Component } from 'vue'
import { createI18n, type LocaleMessageValue, type VueMessageType } from 'vue-i18n'

import en from './locales/en.json'

type Words = Record<string, LocaleMessageValue<VueMessageType>>
type Messages = { default: Words }
type PageModule = { default: Component }
type WordsLoader = (locale: string) => Promise<Messages>

export const locales = ['en', 'nl'] as const
export type Locale = (typeof locales)[number]

function isLocale(value: string): value is Locale {
  return (locales as readonly string[]).includes(value)
}

function browserLocale(): Locale {
  const match: string | undefined = navigator.languages
    .map((tag) => tag.split('-')[0]!)
    .find((tag) => isLocale(tag))

  return match && isLocale(match)
    ? match
    : 'en'
}

const chosenLocale: RemovableRef<string> = useLocalStorage('nnz.locale', browserLocale())

// Only the shared words load up front; each page brings its own file (V10, V17).
// Messages are open: other languages and every page's words arrive later (loadWords).
const startMessages: Record<string, Words> = { en }

export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: startMessages,
})

const sharedWords: WordsLoader = (locale) => import(`./locales/${locale}.json`)
// Every page opened so far: a language switch reloads their words too.
const openedPages: Set<WordsLoader> = new Set([sharedWords])
// The language being switched to. A page that opens mid-switch loads this one, not the one still shown.
let targetLocale: string = 'en'

// A page without a file for this language yet shows its English words (fallbackLocale).
async function loadWords(loader: WordsLoader, locale: string): Promise<void> {
  try {
    i18n.global.mergeLocaleMessage(locale, (await loader(locale)).default)
  } catch (missing) {
    if (locale === 'en')
      throw missing

    await loadWords(loader, 'en')
  }
}

export async function setLocale(locale: Locale): Promise<void> {
  targetLocale = locale
  await Promise.all([...openedPages].map((loader) => loadWords(loader, locale)))
  i18n.global.locale.value = locale
  chosenLocale.value = locale
  document.documentElement.lang = locale
}

// Runs before the first render, so the first page already opens in the chosen language.
export function startLocale(): Promise<void> {
  return setLocale(isLocale(chosenLocale.value)
    ? chosenLocale.value
    : 'en')
}

// A route's lazy component: the page code and the page's words for the current language, in parallel.
export async function loadPage(
  view: () => Promise<PageModule>,
  messages: WordsLoader,
): Promise<PageModule> {
  openedPages.add(messages)
  const [page] = await Promise.all([view(), loadWords(messages, targetLocale)])

  return page
}
