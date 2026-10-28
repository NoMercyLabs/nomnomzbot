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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';
import type { KeyParts } from '@/lib/array';

import { failed, problemError, type ApiError, type ApiOperation } from './errors';

export { failed };

// A refusal carries the bot's own reason in its body (`detail` or `message`); without one, the status stands in.
// eslint-disable-next-line no-restricted-syntax -- every endpoint's error body has a different generated type, so the input must accept any of them
export function refusal(what: ApiOperation, error: unknown, status: number): ApiError {
  return problemError(what, status, error);
}

// The key and reader of a channel query for one known channel id: what the page runs and what a prefetch warms.
export function channelOptions<T>(
  key: (channelId: string | null) => KeyParts,
  fetch: (channelId: string) => Promise<T>,
  channelId: string,
) {
  return {
    queryKey: key(channelId),
    queryFn: () => fetch(channelId),
  } as const satisfies { queryKey: KeyParts; queryFn: () => Promise<T> };
}

// A query that waits for a channel id and keeps one cache entry per channel.
export function channelQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => KeyParts,
  fetch: (channelId: string) => Promise<T>,
  refetchInterval?: number,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => key(toValue(channelId))),
    queryFn: () => fetch(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
    refetchInterval,
  });
}
