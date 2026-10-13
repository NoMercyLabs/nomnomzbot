// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue'
import { createI18n, type LocaleMessageDictionary } from 'vue-i18n'

import en from './locales/en.json'

type Messages = { default: LocaleMessageDictionary<string> }
type PageModule = { default: Component }

// Only the shared words load up front; each page brings its own file (V10, V17).
export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
})

// A route's lazy component: the page code and the page's words for the current language, in parallel.
export async function loadPage(
  view: () => Promise<PageModule>,
  messages: (locale: string) => Promise<Messages>,
): Promise<PageModule> {
  const locale: string = i18n.global.locale.value
  const [page, words] = await Promise.all([view(), messages(locale)])
  i18n.global.mergeLocaleMessage(locale, words.default)
  return page
}
