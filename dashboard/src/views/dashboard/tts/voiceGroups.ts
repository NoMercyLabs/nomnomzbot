// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { voiceFamily, voiceFamilyOrder, type TtsVoice } from '@/api/tts'

export type VoiceListItem =
  { kind: 'heading'; key: string; label: string } | { kind: 'voice'; key: string; voice: TtsVoice }

const FIRST_LOCALE = 'en-US'

// "en-US" in words, in the dashboard language: "English (United States)". A code the runtime cannot name stays as it is.
export function localeLabel(locale: string, language: string): string {
  const code: string = locale.replace('_', '-')

  try {
    return (
      new Intl.DisplayNames([language], { type: 'language', languageDisplay: 'standard' }).of(
        code,
      ) ?? locale
    )
  } catch {
    return locale
  }
}

function localeOrder(a: string, b: string): number {
  if (a === FIRST_LOCALE)
    return b === FIRST_LOCALE
      ? 0
      : -1

  if (b === FIRST_LOCALE)
    return 1

  return a.localeCompare(b)
}

// Voices grouped by locale: language families in the chip order, the United States first, then by locale.
export function groupVoices(voices: readonly TtsVoice[], language: string): VoiceListItem[] {
  const byLocale = new Map<string, TtsVoice[]>()

  for (const voice of voices)
    byLocale.set(voice.locale, [...(byLocale.get(voice.locale) ?? []), voice])

  const locales = [...byLocale.keys()].sort(
    (a: string, b: string) =>
      voiceFamilyOrder.indexOf(voiceFamily(a)) - voiceFamilyOrder.indexOf(voiceFamily(b)) ||
      localeOrder(a, b),
  )

  return locales.flatMap((locale: string): VoiceListItem[] => [
    { kind: 'heading', key: `group:${locale}`, label: localeLabel(locale, language) },
    ...(byLocale.get(locale) ?? []).map((voice: TtsVoice): VoiceListItem => ({
      kind: 'voice',
      key: voice.id,
      voice,
    })),
  ])
}
