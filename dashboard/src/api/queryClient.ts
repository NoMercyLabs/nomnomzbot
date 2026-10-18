// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
