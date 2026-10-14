// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
