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

import { QueryClient } from '@tanstack/vue-query';
import type { DefaultError, QueryExecuteOptions, QueryKey } from '@tanstack/vue-query';

// Cache first (V6): a page shows the last known data at once and refreshes it in the background.
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 30 * 60_000,
      refetchOnWindowFocus: true,
    },
  },
});

// The cached data of a query, fetched once when the cache holds none. A router guard reads data
// this way outside a component. `static` keeps cached data fresh, which is what the deprecated
// `ensureQueryData` did. A fetch is not retried unless the options set `retry`.
export function ensureQuery<
  TQueryFnData,
  TError = DefaultError,
  TData = TQueryFnData,
  TQueryKey extends QueryKey = QueryKey,
>(
  options: QueryExecuteOptions<TQueryFnData, TError, TData, TQueryFnData, TQueryKey>,
): Promise<TData> {
  return queryClient.query({ ...options, staleTime: 'static' });
}
