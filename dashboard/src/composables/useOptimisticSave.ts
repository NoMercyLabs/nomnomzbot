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
  type QueryKey,
  type UseMutationReturnType,
} from '@tanstack/vue-query';

import { type SaveWhat, useSaveErrorsStore } from '@/stores/saveErrors';

export interface OptimisticSaveOptions<TData, TVariables> {
  // One cache entry, or the entry each change names (a toggle on one row of many).
  queryKey: QueryKey | ((variables: TVariables) => QueryKey);
  // Sends the change. What it returns is the value the server now holds.
  mutationFn: (variables: TVariables) => Promise<TData>;
  // The cached value as it looks once the change is accepted. With nothing cached yet, returning undefined leaves the entry empty.
  apply: (current: TData | undefined, variables: TVariables) => TData | undefined;
  titleKey?: string;
  messageKey?: string;
  // What the change was, in words, for the error inbox when it fails.
  describe?: (variables: TVariables) => SaveWhat;
}

export const SAVE_FAILED_TITLE: string = 'frame.inbox.saveFailedTitle';
export const SAVE_FAILED_MESSAGE: string = 'frame.inbox.saveFailedMessage';

// One save to one cache entry: the entry and the save's ticket.
function saveId(queryKey: QueryKey, ticket: number | undefined): string {
  return `${JSON.stringify(queryKey)}#${ticket}`;
}

type EntryState<TData> = {
  newest: number;
  inFlight: number;
  // The last value the server confirmed, or the value before the first save of a burst.
  confirmed: TData | undefined;
};

// Every save is optimistic (V7): the cache changes at once; on an error it rolls back and the error
// reaches the error inbox. Of several saves in flight to one cache entry, only the newest may write to it.
export function useOptimisticSave<TData, TVariables>(
  options: OptimisticSaveOptions<TData, TVariables>,
): UseMutationReturnType<TData, Error, TVariables, number> {
  const queries = useQueryClient();
  const errors = useSaveErrorsStore();
  const entries = new Map<string, EntryState<TData> | undefined>();
  const pages = new Map<string, string>();

  function keyOf(variables: TVariables): QueryKey {
    return typeof options.queryKey === 'function'
      ? options.queryKey(variables)
      : options.queryKey;
  }

  function entryOf(queryKey: QueryKey): EntryState<TData> {
    const id = JSON.stringify(queryKey);
    let entry = entries.get(id);

    if (!entry) {
      entry = {
        newest: 0,
        inFlight: 0,
        confirmed: undefined,
      };

      entries.set(id, entry);
    }

    return entry;
  }

  return useMutation<TData, Error, TVariables, number>({
    mutationFn: options.mutationFn,
    onMutate: async (variables: TVariables): Promise<number> => {
      const queryKey = keyOf(variables);
      const entry = entryOf(queryKey);

      await queries.cancelQueries({ queryKey });

      if (entry.inFlight === 0) {
        entry.confirmed = queries.getQueryData<TData>(queryKey);
      }

      entry.inFlight += 1;
      entry.newest += 1;
      // The page is read when the change is made: a failure that lands after a page switch still names it.
      pages.set(saveId(queryKey, entry.newest), `${location.pathname}${location.search}`);

      queries.setQueryData<TData>(queryKey, (current) =>
        options.apply(current, variables));

      return entry.newest;
    },
    onSuccess: (data: TData, variables: TVariables, ticket: number | undefined): void => {
      const queryKey = keyOf(variables);
      const entry = entryOf(queryKey);

      entry.confirmed = data;

      if (ticket === entry.newest) {
        queries.setQueryData<TData>(queryKey, data);
      }
    },
    onError: (_error: Error, variables: TVariables, ticket: number | undefined): void => {
      const queryKey = keyOf(variables);
      const entry = entryOf(queryKey);

      if (ticket === entry.newest) {
        queries.setQueryData<TData>(queryKey, entry.confirmed);
      }

      const what = options.describe?.(variables) ?? null;
      const where = pages.get(saveId(queryKey, ticket));

      errors.report(
        options.titleKey ?? SAVE_FAILED_TITLE,
        options.messageKey ?? SAVE_FAILED_MESSAGE,
        what,
        where,
      );
    },
    onSettled: (
      _data: TData | undefined,
      _error: Error | null,
      variables: TVariables,
      ticket: number | undefined,
    ): void => {
      const queryKey = keyOf(variables);

      entryOf(queryKey).inFlight -= 1;
      pages.delete(saveId(queryKey, ticket));
    },
  });
}
