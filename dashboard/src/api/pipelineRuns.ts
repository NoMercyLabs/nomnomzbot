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
  useInfiniteQuery,
  useQuery,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { numberOrNull } from '@/lib/number';

import { failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type PipelineRun = components['schemas']['PipelineExecutionSummaryDto'];
export type PipelineRunDetail = components['schemas']['PipelineExecutionDetailDto'];
export type PipelineRunStep = components['schemas']['PipelineExecutionStepLogDto'];

export type PipelineRunPage = {
  runs: PipelineRun[];
  hasMore: boolean;
  nextPage: number | null;
};

const version = '1';
const pageSize = 25;
const firstPage = 1;

export const pipelineRunsKey = (channelId: string | null, pipelineId: string | null): KeyParts => [
  'channels',
  channelId,
  'pipeline-runs',
  pipelineId,
];

async function readRunPage(
  channelId: string,
  pipelineId: string,
  page: number,
): Promise<PipelineRunPage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/pipeline-executions',
    {
      params: {
        path: { version, channelId },
        query: {
          Page: page,
          PageSize: pageSize,
          pipelineId,
        },
      },
    },
  );

  if (error !== undefined)
    throw failed('pipeline runs', response.status);

  return {
    runs: data?.data ?? [],
    hasMore: data?.hasMore ?? false,
    nextPage: numberOrNull(data?.nextPage),
  };
}

// The real runs of one pipeline, newest first: what chat, events and timers set off. A test run is not stored.
export function usePipelineRuns(
  channelId: MaybeRefOrGetter<string | null>,
  pipelineId: MaybeRefOrGetter<string | null>,
): UseInfiniteQueryReturnType<InfiniteData<PipelineRunPage, number>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => pipelineRunsKey(toValue(channelId), toValue(pipelineId))),
    queryFn: ({ pageParam }: { pageParam: number }): Promise<PipelineRunPage> =>
      readRunPage(toValue(channelId) as string, toValue(pipelineId) as string, pageParam),
    initialPageParam: firstPage,
    getNextPageParam: (last: PipelineRunPage, all: PipelineRunPage[]): number | undefined =>
      last.hasMore
        ? (last.nextPage ?? all.length + 1)
        : undefined,
    enabled: computed(() => toValue(channelId) !== null && toValue(pipelineId) !== null),
  });
}

async function readRun(channelId: string, id: string): Promise<PipelineRunDetail> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/pipeline-executions/{id}',
    { params: { path: {
      version,
      channelId,
      id: Number(id),
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('pipeline run', response.status);

  return data.data;
}

// One run with the outcome of each step, so the step that failed can be named.
export function usePipelineRun(
  channelId: MaybeRefOrGetter<string | null>,
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PipelineRunDetail, Error> {
  return useQuery({
    queryKey: computed(() => [...pipelineRunsKey(toValue(channelId), null), 'run', toValue(id)]),
    queryFn: () => readRun(toValue(channelId) as string, toValue(id) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(id) !== null),
    staleTime: Infinity,
  });
}
