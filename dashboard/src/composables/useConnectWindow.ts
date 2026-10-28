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

import { useQueryClient } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { connectionsKey } from '@/api/home';
import { integrationsKey } from '@/api/integrations';
import { openOAuthPopup, type OAuthOutcome } from '@/lib/oauthPopup';

// A connect to a platform or service runs in its own window, so the dashboard stays where it is. When the window
// settles, the connection lists are read again and a failure is named. A browser that blocks the popup gets the
// whole tab instead.
export function useConnectWindow(
  channelId: MaybeRefOrGetter<string | null>,
): (url: string) => void {
  const queries = useQueryClient();
  const { t } = useI18n();

  function settled(outcome: OAuthOutcome): void {
    void queries.invalidateQueries({ queryKey: connectionsKey(toValue(channelId)) });
    void queries.invalidateQueries({ queryKey: integrationsKey(toValue(channelId)) });

    if (outcome.error !== null) {
      toast.error(t('common.connectFailed', { reason: outcome.error }));
    }
  }

  return (url: string): void => {
    if (!openOAuthPopup(url, settled)) {
      window.location.assign(url);
    }
  };
}
