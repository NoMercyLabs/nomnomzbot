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

export type Quote = components['schemas']['Quote'];

// What the dialog saves. createdByUserId is read only on a new quote: an edit keeps the author.
export type QuoteDraft = {
  text: string;
  quotedDisplayName: string | null;
  contextGame: string | null;
  quotedAt: string | null;
  createdByUserId: string | null;
};

export type SaveQuote = { existing: Quote | null; draft: QuoteDraft };

export type QuotePage = {
  quotes: Quote[];
  total: number;
  hasMore: boolean;
};

const version = '1';

export const quotesKey = (channelId: string | null): KeyParts => ['channels', channelId, 'quotes'];

async function readQuotes(search: string, limit: number): Promise<QuotePage> {
  const { data, error, response } = await client.GET('/api/v{version}/quotes', {
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
    throw failed('quotes', response.status, error);

  const quotes = [...(data?.data ?? [])];

  return {
    quotes,
    total: Number(data?.total ?? quotes.length),
    hasMore: data?.hasMore === true,
  };
}

// The channel's quotes, newest first, the first `limit` that match the search text.
export function useQuotes(
  channelId: MaybeRefOrGetter<string | null>,
  search: MaybeRefOrGetter<string>,
  limit: MaybeRefOrGetter<number>,
): UseQueryReturnType<QuotePage, Error> {
  return channelQuery(
    channelId,
    (id) => [...quotesKey(id), toValue(search), toValue(limit)],
    () => readQuotes(toValue(search), toValue(limit)),
  );
}

// One random quote of the channel. The server answers QUOTES_EMPTY when the library has none.
export async function fetchRandomQuote(): Promise<Quote> {
  const { data, error, response } = await client.GET('/api/v{version}/quotes/random', {
    params: { path: { version } },
  });

  if (error !== undefined || !data?.data)
    throw failed('random quote', response.status, error);

  return data.data;
}

async function writeQuote({ existing, draft }: SaveQuote): Promise<void> {
  const body = {
    text: draft.text,
    quotedDisplayName: draft.quotedDisplayName,
    contextGame: draft.contextGame,
    quotedAt: draft.quotedAt,
  };
  const result = existing
    ? await client.PUT('/api/v{version}/quotes/{number}', {
        params: { path: { version, number: Number(existing.number) } },
        body,
      })
    : await client.POST('/api/v{version}/quotes', {
        params: { path: { version } },
        body: { ...body, createdByUserId: draft.createdByUserId },
      });

  if (result.error !== undefined)
    throw failed('save quote', result.response.status, result.error);
}

// Adds or edits a quote; the list is read again after, and again when the hub reports the change.
export function useSaveQuote(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SaveQuote, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, SaveQuote, void>({
    mutationFn: writeQuote,
    onSuccess: () => queries.invalidateQueries({ queryKey: quotesKey(toValue(channelId)) }),
  });
}

// Deletes a quote by its number. The number is never given to another quote.
export function useDeleteQuote(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, number, void> {
  const queries = useQueryClient();

  return useMutation<void, Error, number, void>({
    mutationFn: async (number: number): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/quotes/{number}', {
        params: { path: { version, number } },
      });

      if (error !== undefined)
        throw failed('delete quote', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: quotesKey(toValue(channelId)) }),
  });
}
