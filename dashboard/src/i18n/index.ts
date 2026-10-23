// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage } from '@vueuse/core';
import type { AcceptableValue } from 'reka-ui';
import type { Component } from 'vue';
import { createI18n, type LocaleMessageValue, type VueMessageType } from 'vue-i18n';

import en from './locales/en.json';

type Words = Record<string, LocaleMessageValue<VueMessageType>>;
type Messages = { default: Words };
type PageModule = { default: Component };
type WordsLoader = (locale: string) => Promise<Messages>;

export const locales = ['en', 'nl'] as const;
export type Locale = (typeof locales)[number];

function isLocale(value: string): value is Locale {
  return (locales as readonly string[]).includes(value);
}

function browserLocale(): Locale {
  const match: string | undefined = navigator.languages
    .map((tag) => tag.split('-')[0]!)
    .find((tag) => isLocale(tag));

  return match && isLocale(match)
    ? match
    : 'en';
}

const chosenLocale = useLocalStorage<string>('nnz.locale', browserLocale());

// Only the shared words load up front; each page brings its own file (V10, V17).
// Messages are open: other languages and every page's words arrive later (loadWords).
const startMessages: Record<string, Words> = { en };

export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: startMessages,
});

const sharedWords: WordsLoader = (locale) => import(`./locales/${locale}.json`);
// Every page opened so far: a language switch reloads their words too.
const openedPages = new Set<WordsLoader>([sharedWords]);
// The language being switched to. A page that opens mid-switch loads this one, not the one still shown.
let targetLocale: string = 'en';

// English loads first for every language: a page without a file for this language yet, and a key its file
// lacks, both show the English words (fallbackLocale) instead of the raw key.
async function loadWords(loader: WordsLoader, locale: string): Promise<void> {
  if (locale !== 'en') {
    await loadWords(loader, 'en');
  }

  try {
    i18n.global.mergeLocaleMessage(locale, (await loader(locale)).default);
  } catch (missing) {
    if (locale === 'en')
      throw missing;
  }
}

export async function setLocale(locale: Locale): Promise<void> {
  targetLocale = locale;
  await Promise.all([...openedPages].map((loader) => loadWords(loader, locale)));
  i18n.global.locale.value = locale;
  chosenLocale.value = locale;
  document.documentElement.lang = locale;
}

// Each language is named in its own language, so a reader finds theirs without reading the current one.
export function ownLanguageName(tag: Locale): string {
  return new Intl.DisplayNames([tag], { type: 'language' }).of(tag) ?? tag;
}

// A picker hands over a value it did not check; only a known language switches.
export function chooseLocale(value: AcceptableValue): void {
  if (typeof value === 'string' && isLocale(value)) {
    void setLocale(value);
  }
}

// Runs before the first render, so the first page already opens in the chosen language.
export function startLocale(): Promise<void> {
  return setLocale(isLocale(chosenLocale.value)
    ? chosenLocale.value
    : 'en');
}

// The labels the bot sends as keys (pipeline actions, widget settings), generated from the KMP strings
// by scripts/gen-schema-words.mjs. A page that shows them loads this beside its own words.
export const schemaWords: WordsLoader = (locale) => import(`./schema/${locale}.json`);

// A shared component that needs a words file the page may not have loaded (the picker lists the bot's actions
// by their schema labels): it loads them here, and a language switch reloads them with the pages' words.
export async function ensureWords(loader: WordsLoader): Promise<void> {
  openedPages.add(loader);
  await loadWords(loader, targetLocale);
}

// A route's lazy component: the page code and the page's words for the current language, in parallel.
export async function loadPage(
  view: () => Promise<PageModule>,
  ...messages: WordsLoader[]
): Promise<PageModule> {
  for (const loader of messages) {
    openedPages.add(loader);
  }

  const [page] = await Promise.all([
    view(),
    ...messages.map((loader) => loadWords(loader, targetLocale)),
  ]);

  return page;
}
