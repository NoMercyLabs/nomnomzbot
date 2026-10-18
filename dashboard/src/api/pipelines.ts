// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { JsonObject } from '@/lib/json';
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelOptions, channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { pipelineRunsKey } from './pipelineRuns';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type PipelineListItem = components['schemas']['PipelineListItem'];
export type PipelineBlastRadius = components['schemas']['PipelineBlastRadius'];
// cspell:disable-next-line -- the server names this DTO.
export type PipelineCatalog = components['schemas']['PipelineCatalogue'];
export type PipelineActionDescriptor = components['schemas']['PipelineActionDescriptor'];
export type PipelineActionField = components['schemas']['PipelineActionField'];
export type PipelineTestRun = components['schemas']['TestRunResult'];
export type PipelineTraceStep = components['schemas']['PipelineTraceStep'];

// The graph is a JsonElement on the wire, so its shape is typed here from the server's
// PipelineStepDefinition (Abstractions/Pipeline). A step with a null id is new; the save gives it one.
export const BLOCK_KINDS = [
  'if',
  'switch',
  'switch_case',
  'loop',
  'random_branch',
  'random_case',
  'try',
  'detached_step',
] as const satisfies string[];

export type BlockKind = (typeof BLOCK_KINDS)[number];

export type PipelineAction = { type: string } & JsonObject;

export type PipelineCondition = {
  type: string;
  operator: string;
  left: string;
  right: string;
  negate: boolean;
};

export type PipelineStep = {
  id: string | null;
  parent_step_id: string | null;
  branch: string | null;
  block_kind: BlockKind | null;
  block_config: JsonObject | null;
  order: number;
  action: PipelineAction;
  condition: PipelineCondition | null;
  continue_on_error: boolean;
};

export type PipelineGraph = { steps: PipelineStep[] };

export type Pipeline = Omit<components['schemas']['Pipeline'], 'graph' | 'triggerCount'> & {
  triggerCount: number;
  graph: PipelineGraph;
};

const version = '1';
const pageSize = 100;
const maxPages = 100;

export const pipelinesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'pipelines',
];

async function readPipelines(channelId: string): Promise<PipelineListItem[]> {
  const rows: PipelineListItem[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/pipelines',
      {
        params: { path: { version, channelId }, query: { Page: page, PageSize: pageSize } },
      },
    );

    if (error !== undefined)
      throw failed('pipelines', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

export function usePipelines(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PipelineListItem[], Error> {
  return channelQuery(channelId, pipelinesKey, readPipelines);
}

export const pipelinesOptions = (id: string) => channelOptions(pipelinesKey, readPipelines, id);

export const pipelineKey = (channelId: string | null, id: string | null): KeyParts => [
  'channels',
  channelId,
  'pipeline',
  id,
];

type WireStep = Partial<PipelineStep> & { action?: PipelineAction; order?: number | string };

// The API leaves nulls out, so every optional part of a step is filled back in here.
function parseStep(step: WireStep) {
  return {
    id: step.id ?? null,
    parent_step_id: step.parent_step_id ?? null,
    branch: step.branch ?? null,
    block_kind: step.block_kind ?? null,
    block_config: step.block_config ?? null,
    order: Number(step.order ?? 0),
    action: step.action ?? { type: '' },
    condition: step.condition ?? null,
    continue_on_error: step.continue_on_error ?? false,
  } as const satisfies PipelineStep;
}

function parseGraph(graph: components['schemas']['Pipeline']['graph']) {
  const steps = (graph as { steps?: WireStep[] } | null)?.steps ?? [];

  return { steps: steps.map(parseStep) } as const satisfies PipelineGraph;
}

function parsePipeline(dto: components['schemas']['Pipeline']) {
  return {
    ...dto,
    triggerCount: Number(dto.triggerCount),
    graph: parseGraph(dto.graph),
  } as const satisfies Pipeline;
}

async function readPipeline(channelId: string, id: string): Promise<Pipeline> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/pipelines/{id}',
    { params: { path: {
      version,
      channelId,
      id,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('pipeline', response.status, error);

  return parsePipeline(data.data);
}

export function usePipeline(
  channelId: MaybeRefOrGetter<string | null>,
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Pipeline, Error> {
  return useQuery({
    queryKey: computed(() => pipelineKey(toValue(channelId), toValue(id))),
    queryFn: () => readPipeline(toValue(channelId) as string, toValue(id) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(id) !== null),
  });
}

export type NewPipeline = { name: string };

// A new pipeline starts empty and on; the list reloads so the new row shows.
export function useCreatePipeline(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ name }: NewPipeline): Promise<Pipeline> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/pipelines',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { name, graph: { steps: [] } },
        },
      );

      if (error !== undefined || !data?.data)
        throw refusal('create pipeline', error, response.status);

      return parsePipeline(data.data);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: pipelinesKey(toValue(channelId)) }),
  });
}

// Only the parts named change; the graph goes whole, so new steps come back with their ids.
export type PipelineChange = {
  id: string;
  name?: string;
  description?: string | null;
  isEnabled?: boolean;
  graph?: PipelineGraph;
};

async function writePipeline(
  channelId: string,
  { id, ...change }: PipelineChange,
): Promise<Pipeline> {
  const { data, error, response } = await client.PUT(
    '/api/v{version}/channels/{channelId}/pipelines/{id}',
    {
      params: { path: {
        version,
        channelId,
        id,
      } },
      body: change,
    },
  );

  if (error !== undefined || !data?.data)
    throw refusal('pipeline', error, response.status);

  return parsePipeline(data.data);
}

export function useSavePipeline(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<Pipeline, Error, PipelineChange, number> {
  const queries = useQueryClient();

  return useOptimisticSave<Pipeline, PipelineChange>({
    describe: ({ id, name }) => ({ key: 'savePipeline', params: { name: name ?? id } }),
    queryKey: ({ id }: PipelineChange) => pipelineKey(toValue(channelId), id),
    mutationFn: async (change: PipelineChange): Promise<Pipeline> => {
      const saved = await writePipeline(toValue(channelId) as string, change);

      void queries.invalidateQueries({ queryKey: pipelinesKey(toValue(channelId)) });

      return saved;
    },
    // The workbench saves only a pipeline it has loaded, so the cached one is always there.
    apply: (current: Pipeline | undefined, change: PipelineChange) =>
      ({ ...(current as Pipeline), ...change }) as const satisfies Pipeline,
  });
}

// Deletes the pipeline for good. What started it (commands, triggers, timers, event responses) stops running it.
export function useDeletePipeline(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (id: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/pipelines/{id}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          id,
        } } },
      );

      if (error !== undefined)
        throw refusal('pipeline delete', error, response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: pipelinesKey(toValue(channelId)) }),
  });
}

async function readBlastRadius(channelId: string, id: string): Promise<PipelineBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/pipelines/{id}/blast-radius',
    { params: { path: {
      version,
      channelId,
      id,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('pipeline blast radius', response.status, error);

  return data.data;
}

// What runs this pipeline: the commands, chat triggers, timers and event responses that point at it.
export function useBlastRadius(
  channelId: MaybeRefOrGetter<string | null>,
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PipelineBlastRadius, Error> {
  return useQuery({
    queryKey: computed(() => [...pipelineKey(toValue(channelId), toValue(id)), 'blast-radius']),
    queryFn: () => readBlastRadius(toValue(channelId) as string, toValue(id) as string),
    enabled: computed(() => toValue(channelId) !== null && toValue(id) !== null),
  });
}

export const catalogKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'pipeline-catalog',
];

async function readCatalog(channelId: string): Promise<PipelineCatalog> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/pipelines/actions',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('pipeline catalog', response.status, error);

  return data.data;
}

// Every action a step can run, with the fields its form needs.
export function usePipelineCatalog(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PipelineCatalog, Error> {
  return channelQuery(channelId, catalogKey, readCatalog);
}

export type PipelineOptionList = components['schemas']['PipelineOptionListResult'];
export type PipelineOption = components['schemas']['PipelineOption'];

// The resource kinds the bot can list for a field (PipelineOptionsController).
export const PICKER_KINDS = [
  'reward',
  'widget',
  'voice',
  'sound_clip',
  'pick_list',
  'discord_channel',
  'discord_role',
  'twitch_user',
  'asset',
  'script',
  'music_playlist',
  'twitch_reward',
  'obs_audio_input',
] as const satisfies string[];

export type PickerKind = (typeof PICKER_KINDS)[number];

const optionPage = 50;

// A search narrows the list; a value asks for that one item, so a saved choice shows its name.
// The context holds the step's other choice settings (provider = youtube), so a list can narrow by them.
export type OptionContext = Record<string, string>;

type OptionAsk = {
  search?: string;
  value?: string;
  context?: OptionContext;
};

// The bot reads the context as repeated name=value entries.
function contextEntries(context: OptionContext | undefined): string[] | undefined {
  const entries = Object.entries(context ?? {}).map(([name, text]) =>
    `${name}=${text}`);

  return entries.length > 0
    ? entries
    : undefined;
}

async function readOptions(
  channelId: string,
  kind: PickerKind,
  ask: OptionAsk,
): Promise<PipelineOptionList> {
  const { data, error, response } = await client.GET('/api/v{version}/pipelines/options/{kind}', {
    params: {
      path: { version, kind },
      query: {
        PageSize: optionPage,
        Search: ask.search || undefined,
        value: ask.value,
        context: contextEntries(ask.context),
      },
    },
    // The route carries no channel, so the tenant goes in the header the bot reads it from.
    headers: { 'X-Channel-Id': channelId },
  });

  if (error !== undefined || !data?.data)
    throw failed('pipeline options', response.status, error);

  return data.data;
}

// What a picker field can choose from, narrowed by the search; a source that is not connected says why.
export function usePipelineOptions(
  channelId: MaybeRefOrGetter<string | null>,
  kind: MaybeRefOrGetter<PickerKind>,
  search: MaybeRefOrGetter<string>,
  context: MaybeRefOrGetter<OptionContext> = {},
): UseQueryReturnType<PipelineOptionList, Error> {
  return useQuery({
    queryKey: computed(() =>
      ['channels', toValue(channelId), 'pipeline-options', toValue(kind), toValue(search), toValue(context)]),
    queryFn: () =>
      readOptions(toValue(channelId) as string, toValue(kind), {
        search: toValue(search),
        context: toValue(context),
      }),
    enabled: computed(() => toValue(channelId) !== null),
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });
}

// The one item a saved setting names, for a choice beyond the first page of the list.
export function usePipelineOption(
  channelId: MaybeRefOrGetter<string | null>,
  kind: MaybeRefOrGetter<PickerKind>,
  value: MaybeRefOrGetter<string | null>,
  context: MaybeRefOrGetter<OptionContext> = {},
): UseQueryReturnType<PipelineOption | null, Error> {
  return useQuery({
    queryKey: computed(() =>
      ['channels', toValue(channelId), 'pipeline-options', toValue(kind), 'value', toValue(value), toValue(context)]),
    queryFn: async () => {
      const list = await readOptions(toValue(channelId) as string, toValue(kind), {
        value: toValue(value) as string,
        context: toValue(context),
      });

      return list.items[0] ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null && !!toValue(value)),
    staleTime: 60_000,
  });
}

export type TestRunRequest = {
  id: string;
  variables: Record<string, string>;
  graph?: PipelineGraph;
};

// The bot runs the graph without real side effects and returns what each step did: the draft on the canvas
// when one is sent, the saved graph otherwise.
export function useTestRunPipeline(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation({
    // The bot keeps every test run under its pipeline's Runs, so the list shows it at once.
    onSuccess: (_run: PipelineTestRun, { id }: TestRunRequest) =>
      queries.invalidateQueries({ queryKey: pipelineRunsKey(toValue(channelId), id) }),
    mutationFn: async ({ id, variables, graph }: TestRunRequest): Promise<PipelineTestRun> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/pipelines/{id}/test-run',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            id,
          } },
          body: { variables, graph },
        },
      );

      if (error !== undefined || !data?.data)
        throw refusal('pipeline test run', error, response.status);

      return data.data;
    },
  });
}
