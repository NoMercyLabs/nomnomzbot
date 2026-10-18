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
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type ErasureRequest = components['schemas']['ErasureRequest'];
export type ErasureRequestSummary = components['schemas']['ErasureRequestSummary'];
export type ErasurePreview = components['schemas']['ErasurePreview'];

export type DataRequestFilter = {
  status: string;
  requestType: string;
};

const version = '1';

export const dataRequestsPrefix: KeyParts = ['admin', 'dataRequests'];

// The filter value that means "no narrowing"; the query leaves the parameter out.
export const allRequests = 'all';

function narrowed(value: string): string | undefined {
  return value === allRequests
    ? undefined
    : value;
}

export function useErasureRequests(
  filter: MaybeRefOrGetter<DataRequestFilter>,
): UseQueryReturnType<ErasureRequest[], Error> {
  return useQuery({
    queryKey: computed(() => merge(dataRequestsPrefix, ['list', toValue(filter)])),
    queryFn: async (): Promise<ErasureRequest[]> => {
      const { status, requestType } = toValue(filter);
      const { data, error, response } = await client.GET('/api/v{version}/compliance/erasure', {
        params: {
          path: { version },
          query: {
            PageSize: 50,
            status: narrowed(status),
            requestType: narrowed(requestType),
          },
        },
      });

      if (error !== undefined)
        throw problemError('data requests list', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useErasureSummary(): UseQueryReturnType<ErasureRequestSummary | null, Error> {
  return useQuery({
    queryKey: merge(dataRequestsPrefix, ['summary']),
    queryFn: async (): Promise<ErasureRequestSummary | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/compliance/erasure/summary',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('data requests summary', response.status, error);

      return data?.data ?? null;
    },
  });
}

// The counted blast radius of erasing one subject, read before the operator confirms a retry.
export function useErasurePreview(
  request: MaybeRefOrGetter<ErasureRequest | null>,
): UseQueryReturnType<ErasurePreview | null, Error> {
  return useQuery({
    queryKey: computed(() => merge(dataRequestsPrefix, ['preview', toValue(request)?.id])),
    enabled: computed(() => toValue(request) !== null),
    queryFn: async (): Promise<ErasurePreview | null> => {
      const target = toValue(request)!;
      const { data, error, response } = await client.GET(
        '/api/v{version}/compliance/erasure/preview',
        {
          params: {
            path: { version },
            query: {
              subjectUserId: target.subjectUserId,
              broadcasterId: target.broadcasterId ?? undefined,
            },
          },
        },
      );

      if (error !== undefined)
        throw problemError('data requests preview', response.status, error);

      return data?.data ?? null;
    },
  });
}

export function useRetryErasure() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (erasureRequestId: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/compliance/erasure/{erasureRequestId}/retry',
        { params: { path: { version, erasureRequestId } } },
      );

      if (error !== undefined)
        throw problemError('data requests retry', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: dataRequestsPrefix }),
  });
}
