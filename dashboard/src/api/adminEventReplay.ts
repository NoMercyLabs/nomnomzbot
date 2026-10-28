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
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type ReplayableProjection = components['schemas']['AdminReplayableProjection'];
export type ReplayPreview = components['schemas']['AdminEventReplayPreview'];
export type ReplayResult = components['schemas']['AdminEventReplayResult'];
export type ReplayAuditEntry = components['schemas']['IamAuditEntry'];

// The scope of one replay: one channel, one projection, a time window and an optional event type.
export type ReplayScope = {
  broadcasterId: string;
  projectionName: string;
  fromUtc: string;
  toUtc: string;
  eventType: string | null;
};

const version = '1';

// The permission the server writes into the audit log for every executed replay.
export const replayAuditPermission = 'eventstore:admin-replay';

export const eventReplayPrefix: KeyParts = ['admin', 'eventReplay'];

export function useReplayableProjections(): UseQueryReturnType<ReplayableProjection[], Error> {
  return useQuery({
    queryKey: merge(eventReplayPrefix, ['projections']),
    queryFn: async (): Promise<ReplayableProjection[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/eventstore/projections',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('event replay projections', response.status, error);

      return data?.data ?? [];
    },
  });
}

// The newest executed replays, read from the audit log the server writes on every run.
export function useReplayHistory(): UseQueryReturnType<ReplayAuditEntry[], Error> {
  return useQuery({
    queryKey: merge(eventReplayPrefix, ['history']),
    queryFn: async (): Promise<ReplayAuditEntry[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/audit', {
        params: {
          path: { version },
          query: { permission: replayAuditPermission, Take: 10 },
        },
      });

      if (error !== undefined)
        throw problemError('event replay history', response.status, error);

      return data?.data ?? [];
    },
  });
}

// Counts the events a scope would replay. Nothing is replayed.
export function useReplayPreview() {
  return useMutation({
    mutationFn: async (scope: ReplayScope): Promise<ReplayPreview> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/eventstore/replay/preview',
        { params: { path: { version } }, body: scope },
      );

      if (error !== undefined || data?.data === undefined || data.data === null)
        throw problemError('event replay preview', response.status, error);

      return data.data;
    },
  });
}

// Runs the replay. The server refuses it unless the count still matches the one the operator saw.
export function useReplayExecute() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (input: {
      scope: ReplayScope;
      expectedCount: number;
    }): Promise<ReplayResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/eventstore/replay/execute',
        {
          params: { path: { version } },
          body: { ...input.scope, expectedCount: input.expectedCount },
        },
      );

      if (error !== undefined || data?.data === undefined || data.data === null)
        throw problemError('event replay execute', response.status, error);

      return data.data;
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: eventReplayPrefix }),
  });
}
