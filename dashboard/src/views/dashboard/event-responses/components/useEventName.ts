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
