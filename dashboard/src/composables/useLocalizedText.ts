// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { useI18n } from 'vue-i18n';

import type { components } from '@/api/schema';

export type LocalizedText = components['schemas']['LocalizedText'];

export type TextOf = (text: LocalizedText | null | undefined, fallback: string) => string;

// A label the bot names with a LocalizedText, in this order: the text the bot sent, the translation of its key
// (the schema words, see scripts/gen-schema-words.mjs), then the fallback.
export function useLocalizedText(): TextOf {
  const { t, te } = useI18n();

  return (text: LocalizedText | null | undefined, fallback: string): string => {
    if (text?.text)
      return text.text;

    if (text?.key && te(text.key))
      return t(text.key);

    return fallback;
  };
}
