// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useQuery,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ActionRequiredItem = components['schemas']['ActionRequiredItemDto'];

export const actionRequiredKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'action-required',
];

const version = '1';

async function readActionRequired(channelId: string): Promise<ActionRequiredItem[]> {
  const { data } = await client.GET(
    '/api/v{version}/channels/{channelId}/notifications/action-required',
    {
      params: { path: { version, channelId } },
    },
  );

  return data?.data ?? [];
}

// Dismisses one item by its stable id. The list drops it at once and takes the server's list after;
// the server hides a dismissed item until a new occurrence of it appears.
export function useDismissActionRequired(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<ActionRequiredItem[], Error, string, number> {
  return useOptimisticSave<ActionRequiredItem[], string>({
    queryKey: () => actionRequiredKey(toValue(channelId)),
    mutationFn: async (itemId: string): Promise<ActionRequiredItem[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/notifications/action-required/dismiss',
        {
          params: { path: { version, channelId: id } },
          body: { ids: [itemId] },
        },
      );

      if (error !== undefined)
        throw failed('action-required-dismiss', response.status);

      return readActionRequired(id);
    },
    apply: (current: ActionRequiredItem[] | undefined, itemId: string): ActionRequiredItem[] =>
      (current ?? []).filter((item: ActionRequiredItem) => item.id !== itemId),
  });
}

// The items that need the person now (C9): the source of the error inbox in the frame.
export function useActionRequired(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActionRequiredItem[], Error> {
  return useQuery({
    queryKey: computed(() => actionRequiredKey(toValue(channelId))),
    queryFn: (): Promise<ActionRequiredItem[]> => readActionRequired(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
    refetchInterval: 60_000,
  });
}
