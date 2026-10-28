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

// The words for a provider id the server sends (twitch, spotify, edge, ...).
// A provider this client has no name for yet shows its id as the server sent it.
export function useProviderName(): (id: string) => string {
  const { t, te } = useI18n();

  return (id: string): string => {
    const key = `common.provider.${id}`;

    return te(key)
      ? t(key)
      : id;
  };
}
