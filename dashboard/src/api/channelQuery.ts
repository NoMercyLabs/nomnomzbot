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

export function failed(what: string, status: number): Error {
  return new Error(`${what} ${status}`);
}

// A refusal carries the bot's own reason in `message`; without one, the status stands in.
export function refusal(what: string, error: unknown, status: number): Error {
  const reason = (error as { message?: string | null } | undefined)?.message;

  return reason
    ? new Error(reason)
    : failed(what, status);
}

// A query that waits for a channel id and keeps one cache entry per channel.
export function channelQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => unknown[],
  fetch: (channelId: string) => Promise<T>,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => key(toValue(channelId))),
    queryFn: () => fetch(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  });
}
