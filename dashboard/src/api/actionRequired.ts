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

import type { components } from './schema';
import { client } from './client';

export type ActionRequiredItem = components['schemas']['ActionRequiredItemDto'];

export const actionRequiredKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'action-required',
];

// The items that need the person now (C9): the source of the error inbox in the frame.
export function useActionRequired(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActionRequiredItem[], Error> {
  return useQuery({
    queryKey: computed(() => actionRequiredKey(toValue(channelId))),
    queryFn: async (): Promise<ActionRequiredItem[]> => {
      const { data } = await client.GET(
        '/api/v{version}/channels/{channelId}/notifications/action-required',
        {
          params: { path: { version: '1', channelId: toValue(channelId) as string } },
        },
      );

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null),
    refetchInterval: 60_000,
  });
}
