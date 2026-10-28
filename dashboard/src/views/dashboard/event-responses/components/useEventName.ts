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

import { nameOf } from './eventResponseDraft';

// An event type in words: the page's own name for it, else the words of its key.
export function useEventName(): (eventType: string) => string {
  const { t, te } = useI18n();

  return (eventType: string): string => {
    const key = `dashboard.eventResponses.names.${eventType.replace(/\./g, '_')}`;

    return te(key)
      ? t(key)
      : nameOf(eventType);
  };
}
