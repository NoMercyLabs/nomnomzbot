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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ChannelDiagnostics = components['schemas']['ChannelDiagnostics'];
export type ChannelDiagnosticSummary = components['schemas']['ChannelDiagnosticSummary'];
export type ChannelRuns = components['schemas']['ChannelDiagnosticRuns'];
export type ChannelRun = components['schemas']['PipelineExecutionSummary'];
export type ChannelRunDetail = components['schemas']['PipelineExecutionDetail'];
export type ChannelRunStep = components['schemas']['PipelineExecutionStepLog'];
export type ChannelRunHostCall = components['schemas']['PipelineExecutionHostCall'];
export type ChannelTts = components['schemas']['ChannelDiagnosticTts'];
export type ChannelTtsClip = components['schemas']['ChannelDiagnosticTtsClip'];
export type ChannelTtsWindow = components['schemas']['ChannelDiagnosticTtsWindow'];

// The sections the bot can answer. One read takes any of them, so a new tab asks for its section here
// and never adds a second call.
export type DiagnosticSection = 'summary' | 'runs' | 'tts';

// What the runs section is narrowed by: one page, failed runs only, one pipeline ('' means every pipeline).
export type DiagnosticRunsQuery = {
  page: number;
  pageSize: number;
  failuresOnly: boolean;
  pipelineId: string;
};

const version = '1';

export const channelDiagnosticsKey = (
  id: string | null,
  sections: readonly DiagnosticSection[],
  runs?: DiagnosticRunsQuery,
): KeyParts => ['admin', 'channel', id, 'diagnostics', sections.join(','), runs];

// The requested sections of one channel. Each read writes one audit row on the bot, so a page asks once
// and shares the answer through the cache. `runs` narrows the runs section when it is asked for.
export function useChannelDiagnostics(
  id: MaybeRefOrGetter<string | null>,
  sections: readonly DiagnosticSection[],
  runs?: MaybeRefOrGetter<DiagnosticRunsQuery>,
): UseQueryReturnType<ChannelDiagnostics | null, Error> {
  return useQuery({
    queryKey: computed(() => channelDiagnosticsKey(toValue(id), sections, toValue(runs))),
    queryFn: async (): Promise<ChannelDiagnostics | null> => {
      const narrow = toValue(runs);
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/channels/{broadcasterId}/diagnostics',
        {
          params: {
            path: { version, broadcasterId: toValue(id)! },
            query: {
              sections: sections.join(','),
              Page: narrow?.page,
              PageSize: narrow?.pageSize,
              failuresOnly: narrow?.failuresOnly || undefined,
              pipelineId: narrow?.pipelineId || undefined,
            },
          },
        },
      );

      if (!response.ok)
        throw problemError('admin channel diagnostics', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(id) !== null),
  });
}

// One run of a channel with its ordered steps and each step's host calls. A run written before the failure
// shape reads a null failure kind and no host calls, which is a normal answer, not an error.
export function useChannelRun(
  id: MaybeRefOrGetter<string | null>,
  runId: MaybeRefOrGetter<number | null>,
): UseQueryReturnType<ChannelRunDetail | null, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'channel', toValue(id), 'diagnostics', 'run', toValue(runId)]),
    queryFn: async (): Promise<ChannelRunDetail | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/channels/{broadcasterId}/diagnostics/runs/{runId}',
        {
          params: {
            path: {
              version,
              broadcasterId: toValue(id)!,
              runId: toValue(runId)!,
            },
          },
        },
      );

      if (!response.ok)
        throw problemError('admin channel run', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(id) !== null && toValue(runId) !== null),
  });
}
