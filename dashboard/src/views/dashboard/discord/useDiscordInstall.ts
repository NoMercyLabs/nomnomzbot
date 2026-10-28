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
import { toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { discordConnectionsKey } from '@/api/discord';

import { installFailure } from './installError';
import { openOAuthPopup, type OAuthOutcome } from '@/lib/oauthPopup';
import { useDiscordInstallUrl } from './installUrl';

interface DiscordInstall {
  // The install address: a plain link when the browser blocks the popup.
  url: ComputedRef<string>;
  // Click handler for the install link: opens the popup and keeps the page where it is.
  start: (event: MouseEvent) => void;
}

// Adding the bot happens in a popup, so the dashboard stays put. When the popup settles, the page reads the
// linked server again and shows the reason if the install failed.
export function useDiscordInstall(channelId: MaybeRefOrGetter<string | null>) {
  const url = useDiscordInstallUrl(channelId);
  const queries = useQueryClient();

  function settled(outcome: OAuthOutcome): void {
    installFailure.value = outcome.error;
    void queries.invalidateQueries({ queryKey: discordConnectionsKey(toValue(channelId)) });
  }

  function start(event: MouseEvent): void {
    installFailure.value = null;

    if (openOAuthPopup(url.value, settled)) {
      event.preventDefault();
    }
  }

  return { url, start } as const satisfies DiscordInstall;
}
