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

import {
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The channel's named pick lists: the lists behind the {list.pick.<name>} helper in a reply.
export type PickList = components['schemas']['PickList'];
export type PickListBlastRadius = components['schemas']['BlastRadius'];

// What the dialog saves: the full state of the list. The name can change; the items replace the old ones.
export type PickListDraft = {
  name: string;
  description: string | null;
  items: string[];
};

export type SavePickList = { existing: PickList | null; draft: PickListDraft };

export type PickListPage = {
  lists: PickList[];
  total: number;
  hasMore: boolean;
};

const version = '1';

export const pickListsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'picklists',
];

async function readPickLists(search: string, limit: number): Promise<PickListPage> {
  const { data, error, response } = await client.GET('/api/v{version}/picklists', {
    params: {
      path: { version },
      query: {
        Page: 1,
        PageSize: limit,
        Search: search === ''
          ? undefined
          : search,
      },
    },
  });

  if (error !== undefined)
    throw failed('pick lists', response.status, error);

  const lists = [...(data?.data ?? [])];

  return {
    lists,
    total: Number(data?.total ?? lists.length),
    hasMore: data?.hasMore === true,
  };
}

// The channel's pick lists, the first `limit` that match the search text.
export function usePickLists(
  channelId: MaybeRefOrGetter<string | null>,
  search: MaybeRefOrGetter<string>,
  limit: MaybeRefOrGetter<number>,
): UseQueryReturnType<PickListPage, Error> {
  return channelQuery(
    channelId,
    (id) => [...pickListsKey(id), toValue(search), toValue(limit)],
    () => readPickLists(toValue(search), toValue(limit)),
  );
}

async function writePickList({ existing, draft }: SavePickList): Promise<void> {
  const result = existing
    ? await client.PUT('/api/v{version}/picklists/{id}', {
        params: { path: { version, id: existing.id } },
        body: draft,
      })
    : await client.POST('/api/v{version}/picklists', {
        params: { path: { version } },
        body: draft,
      });

  if (result.error !== undefined)
    throw failed('save pick list', result.response.status, result.error);
}

// Adds or edits a pick list; the list is read again after.
export function useSavePickList(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SavePickList, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, SavePickList, void>({
    mutationFn: writePickList,
    onSuccess: () => queries.invalidateQueries({ queryKey: pickListsKey(toValue(channelId)) }),
  });
}

// What else uses a pick list: the counted places a delete would break.
export async function fetchPickListBlastRadius(id: string): Promise<PickListBlastRadius> {
  const { data, error, response } = await client.GET('/api/v{version}/picklists/{id}/blast-radius', {
    params: { path: { version, id } },
  });

  if (error !== undefined || !data?.data)
    throw failed('pick list blast radius', response.status, error);

  return data.data;
}

// The blast radius is read again right before the delete: a list whose impact cannot be read is not deleted.
export function useDeletePickList(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, void>({
    mutationFn: async (id: string): Promise<void> => {
      await fetchPickListBlastRadius(id);

      const { error, response } = await client.DELETE('/api/v{version}/picklists/{id}', {
        params: { path: { version, id } },
      });

      if (error !== undefined)
        throw failed('delete pick list', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: pickListsKey(toValue(channelId)) }),
  });
}
