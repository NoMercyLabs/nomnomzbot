// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, watch, type ComputedRef } from 'vue';
import { useRoute } from 'vue-router';

import { useCurrentChannel, type ChannelChoice } from '@/composables/useCurrentChannel';
import { routeChannel } from '@/router/channelRoutes';

export type RouteChannel = {
  // The channel name in the address, or null on a short route.
  name: ComputedRef<string | null>;
  // The list is loaded and the person has no access to the channel in the address.
  denied: ComputedRef<boolean>;
};

// A /app/u/<name>/... address makes that channel the current one, once, when the person has access.
export function useRouteChannel() {
  const route = useRoute();
  const { channels, current, isLoaded, choose } = useCurrentChannel();

  const name = computed<string | null>(() => routeChannel(route));

  const match = computed<ChannelChoice | null>(
    () =>
      channels.value.find((row) => row.login.toLowerCase() === name.value?.toLowerCase()) ?? null,
  );

  const denied = computed<boolean>(
    () => name.value !== null && isLoaded.value && match.value === null,
  );

  watch(
    [match, current],
    async () => {
      const wanted = match.value;

      if (wanted === null || wanted.login === current.value?.login) return;

      await choose(wanted);
    },
    { immediate: true },
  );

  return { name, denied } as const satisfies RouteChannel;
}
