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

import { useMutation, useQuery, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The reports viewers file about a chatter. Moderators work the queue: open reports wait, a dismissed or escalated
// one keeps its place in the history.
export type ViewerReport = components['schemas']['ViewerReport'];

export const REPORT_STATUSES = ['open', 'escalated', 'dismissed'] as const;
export type ReportStatus = (typeof REPORT_STATUSES)[number];

export const REPORT_ACTIONS = ['dismiss', 'escalate'] as const;
export type ReportAction = (typeof REPORT_ACTIONS)[number];

const version = '1';

export const reportsKey = (channelId: string | null, status?: ReportStatus): KeyParts =>
  status === undefined
    ? ['channels', channelId, 'reports']
    : ['channels', channelId, 'reports', status];

export function useReports(
  channelId: MaybeRefOrGetter<string | null>,
  status: MaybeRefOrGetter<ReportStatus>,
): UseQueryReturnType<ViewerReport[], Error> {
  return useQuery({
    queryKey: computed(() => reportsKey(toValue(channelId), toValue(status))),
    queryFn: async (): Promise<ViewerReport[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/moderation/reports',
        {
          params: {
            path: { version, channelId: toValue(channelId) as string },
            query: { status: toValue(status) },
          },
        },
      );

      if (error !== undefined)
        throw failed('reports', response.status, error);

      return [...(data?.data ?? [])];
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}

export type ResolveReport = { report: ViewerReport; action: ReportAction };

// A resolved report leaves the open queue and shows in the history list of its new status.
export function useResolveReport(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ report, action }: ResolveReport): Promise<void> => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/moderation/reports/{reportId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              reportId: report.id,
            },
          },
          body: { action },
        },
      );

      if (error !== undefined)
        throw failed('resolve report', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: reportsKey(toValue(channelId)) }),
  });
}
