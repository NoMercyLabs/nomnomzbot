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
