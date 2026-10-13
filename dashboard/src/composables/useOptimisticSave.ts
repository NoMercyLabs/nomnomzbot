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
  useMutation,
  useQueryClient,
  type QueryKey,
  type UseMutationReturnType,
} from '@tanstack/vue-query'

import { useSaveErrorsStore } from '@/stores/saveErrors'

export interface OptimisticSaveOptions<TData, TVariables> {
  // One cache entry, or the entry each change names (a toggle on one row of many).
  queryKey: QueryKey | ((variables: TVariables) => QueryKey)
  // Sends the change. What it returns is the value the server now holds.
  mutationFn: (variables: TVariables) => Promise<TData>
  // The cached value as it looks once the change is accepted.
  apply: (current: TData | undefined, variables: TVariables) => TData
  titleKey?: string
  messageKey?: string
}

export const SAVE_FAILED_TITLE: string = 'frame.inbox.saveFailedTitle'
export const SAVE_FAILED_MESSAGE: string = 'frame.inbox.saveFailedMessage'

type EntryState<TData> = {
  newest: number
  inFlight: number
  // The last value the server confirmed, or the value before the first save of a burst.
  confirmed: TData | undefined
}

// Every save is optimistic (V7): the cache changes at once; on an error it rolls back and the error
// reaches the error inbox. Of several saves in flight to one cache entry, only the newest may write to it.
export function useOptimisticSave<TData, TVariables>(
  options: OptimisticSaveOptions<TData, TVariables>,
): UseMutationReturnType<TData, Error, TVariables, number> {
  const queries = useQueryClient()
  const errors = useSaveErrorsStore()
  const entries = new Map()

  function keyOf(variables: TVariables): QueryKey {
    return typeof options.queryKey === 'function' ? options.queryKey(variables) : options.queryKey
  }

  function entryOf(queryKey: QueryKey): EntryState<TData> {
    const id = JSON.stringify(queryKey)
    let entry: EntryState<TData> | undefined = entries.get(id)

    if (!entry) {
      entry = { newest: 0, inFlight: 0, confirmed: undefined }
      entries.set(id, entry)
    }

    return entry
  }

  return useMutation<TData, Error, TVariables, number>({
    mutationFn: options.mutationFn,
    onMutate: async (variables: TVariables): Promise<number> => {
      const queryKey = keyOf(variables)
      const entry = entryOf(queryKey)

      await queries.cancelQueries({ queryKey })
      if (entry.inFlight === 0) entry.confirmed = queries.getQueryData<TData>(queryKey)
      entry.inFlight += 1
      entry.newest += 1
      queries.setQueryData<TData>(queryKey, (current) => options.apply(current, variables))

      return entry.newest
    },
    onSuccess: (data: TData, variables: TVariables, ticket: number | undefined): void => {
      const queryKey = keyOf(variables)
      const entry = entryOf(queryKey)

      entry.confirmed = data
      if (ticket === entry.newest) queries.setQueryData<TData>(queryKey, data)
    },
    onError: (_error: Error, variables: TVariables, ticket: number | undefined): void => {
      const queryKey = keyOf(variables)
      const entry = entryOf(queryKey)

      if (ticket === entry.newest) queries.setQueryData<TData>(queryKey, entry.confirmed)
      errors.report(
        options.titleKey ?? SAVE_FAILED_TITLE,
        options.messageKey ?? SAVE_FAILED_MESSAGE,
      )
    },
    onSettled: (_data: TData | undefined, _error: Error | null, variables: TVariables): void => {
      entryOf(keyOf(variables)).inFlight -= 1
    },
  })
}
