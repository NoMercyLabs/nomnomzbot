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
  type QueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';

// The channel's blocked terms: Twitch holds the list, so every read is live and every write answers with the new list.
export const blockedTermsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'blocked-terms',
];

const version = '1';

async function fetchBlockedTerms(channelId: string): Promise<string[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/blocked-terms',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('blocked terms', response.status, error);

  return data?.data ?? [];
}

export function useBlockedTerms(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<string[], Error> {
  return channelQuery(channelId, blockedTermsKey, fetchBlockedTerms);
}

// A write answers with Twitch's new list, so the cache takes it as is.
function keepList(queries: QueryClient, channelId: string | null, terms: string[]): void {
  const key = blockedTermsKey(channelId);

  queries.setQueryData(key, terms);
}

export function useAddBlockedTerm(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (term: string): Promise<string[]> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/moderation/blocked-terms',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { term },
        },
      );

      if (!response.ok)
        throw failed('add blocked term', response.status, error);

      return data?.data ?? [];
    },
    onSuccess: (terms: string[]) => keepList(queries, toValue(channelId), terms),
  });
}

export function useRemoveBlockedTerm(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (term: string): Promise<string[]> => {
      const { data, error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/moderation/blocked-terms/{term}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          term,
        } } },
      );

      if (!response.ok)
        throw failed('remove blocked term', response.status, error);

      return data?.data ?? [];
    },
    onSuccess: (terms: string[]) => keepList(queries, toValue(channelId), terms),
  });
}
