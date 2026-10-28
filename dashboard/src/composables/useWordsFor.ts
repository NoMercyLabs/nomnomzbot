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

// The words for a value the server sends as a plain string (a status, a category). The words live in
// the resource keys `<prefix>.<value>`; a value this client has no words for yet shows as the server sent it.
export function useWordsFor(prefix: string): (value: string) => string {
  const { t, te } = useI18n();

  return (value: string): string => {
    const key = `${prefix}.${value.toLowerCase().replaceAll(/[^a-z0-9]+/g, '_')}`;

    return te(key)
      ? t(key)
      : value;
  };
}
