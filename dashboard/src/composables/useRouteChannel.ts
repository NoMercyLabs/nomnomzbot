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
