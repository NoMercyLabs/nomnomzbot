// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQueryClient } from '@tanstack/vue-query';
import { toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { discordConnectionsKey } from '@/api/discord';

import { installFailure } from './installError';
import { openInstallPopup, type InstallOutcome } from './installPopup';
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

  function settled(outcome: InstallOutcome): void {
    installFailure.value = outcome.error;
    void queries.invalidateQueries({ queryKey: discordConnectionsKey(toValue(channelId)) });
  }

  function start(event: MouseEvent): void {
    installFailure.value = null;

    if (openInstallPopup(url.value, settled)) {
      event.preventDefault();
    }
  }

  return { url, start } as const satisfies DiscordInstall;
}
