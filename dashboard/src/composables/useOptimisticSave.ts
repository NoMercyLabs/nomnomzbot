// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQueryClient, type QueryClient, type QueryKey, type UseMutationReturnType } from '@tanstack/vue-query'

import { useSaveErrorsStore } from '@/stores/saveErrors'

export interface OptimisticSaveOptions<TData, TVariables> {
  queryKey: QueryKey
  // Sends the change. What it returns is the value the server now holds.
  mutationFn: (variables: TVariables) => Promise<TData>
  // The cached value as it looks once the change is accepted.
  apply: (current: TData | undefined, variables: TVariables) => TData
  titleKey?: string
  messageKey?: string
}

export const SAVE_FAILED_TITLE: string = 'frame.inbox.saveFailedTitle'
export const SAVE_FAILED_MESSAGE: string = 'frame.inbox.saveFailedMessage'

// Every save is optimistic (V7): the cache changes at once; on an error it rolls back and the error
// reaches the error inbox. Of several saves in flight, only the newest may write to the cache.
export function useOptimisticSave<TData, TVariables>(
  options: OptimisticSaveOptions<TData, TVariables>,
): UseMutationReturnType<TData, Error, TVariables, number> {
  const queries: QueryClient = useQueryClient()
  const errors = useSaveErrorsStore()
  let newest: number = 0
  let inFlight: number = 0
  // The last value the server confirmed, or the value before the first save of a burst.
  let confirmed: TData | undefined

  return useMutation<TData, Error, TVariables, number>({
    mutationFn: options.mutationFn,
    onMutate: async (variables: TVariables): Promise<number> => {
      await queries.cancelQueries({ queryKey: options.queryKey })
      if (inFlight === 0) confirmed = queries.getQueryData<TData>(options.queryKey)
      inFlight += 1
      newest += 1
      queries.setQueryData<TData>(options.queryKey, (current) => options.apply(current, variables))
      return newest
    },
    onSuccess: (data: TData, _variables: TVariables, ticket: number | undefined): void => {
      confirmed = data
      if (ticket === newest) queries.setQueryData<TData>(options.queryKey, data)
    },
    onError: (_error: Error, _variables: TVariables, ticket: number | undefined): void => {
      if (ticket === newest) queries.setQueryData<TData>(options.queryKey, confirmed)
      errors.report(options.titleKey ?? SAVE_FAILED_TITLE, options.messageKey ?? SAVE_FAILED_MESSAGE)
    },
    onSettled: (): void => {
      inFlight -= 1
    },
  })
}
