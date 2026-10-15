// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { failed, problemError, type ApiError, type ApiOperation } from './errors';

export { failed };

// A refusal carries the bot's own reason in its body (`detail` or `message`); without one, the status stands in.
export function refusal(what: ApiOperation, error: unknown, status: number): ApiError {
  return problemError(what, status, error);
}

// A query that waits for a channel id and keeps one cache entry per channel.
export function channelQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => unknown[],
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
