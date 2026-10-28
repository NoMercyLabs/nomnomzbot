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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { createPinia, setActivePinia } from 'pinia';
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useSaveErrorsStore } from '@/stores/saveErrors';

import {
  pipelineKey,
  pipelinesKey,
  useCreatePipeline,
  usePipeline,
  useSavePipeline,
  useTestRunPipeline,
  type Pipeline,
  type PipelineStep,
} from '../pipelines';
import { onTheWire } from './wire';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('../client', () => ({ client: http }));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const channel = 'c1';

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

function mount<T>(use: () => T): {
  queries: QueryClient;
  hook: T;
  inbox: ReturnType<typeof useSaveErrorsStore>;
} {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const pinia = createPinia();

  setActivePinia(pinia);
  let hook!: T;
  const app: App = createApp({
    setup() {
      hook = use();

      return () => null;
    },
  });

  app.use(pinia);
  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return {
    queries,
    hook,
    inbox: useSaveErrorsStore(),
  };
}

function step(over: Partial<PipelineStep>): PipelineStep {
  return {
    id: null,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'send_message', message: 'hi' },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
    ...over,
  };
}

// An if block with one step in each lane, as the bot stores it.
const storedSteps = [
  step({
    id: 'if-1',
    block_kind: 'if',
    stop_on_match: true,
    action: { type: 'if' },
    condition: {
      type: 'comparison',
      operator: 'gt',
      left: '{user.points}',
      right: '100',
      negate: false,
    },
  }),
  step({
    id: 'then-1',
    parent_step_id: 'if-1',
    branch: 'then',
    action: { type: 'send_message', message: 'rich' },
  }),
  step({
    id: 'else-1',
    parent_step_id: 'if-1',
    branch: 'else',
    order: 1,
    continue_on_error: true,
  }),
];

function dto(steps: PipelineStep[]) {
  return {
    id: 'p1',
    channelId: channel,
    name: 'Shout',
    isEnabled: true,
    triggerKind: 'command',
    graph: { steps },
    triggerCount: '4',
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-02T00:00:00Z',
  } as const satisfies Record<string, unknown>;
}

beforeEach(() => {
  http.GET.mockReset();
  http.POST.mockReset();
  http.PUT.mockReset();
});

describe('usePipeline', () => {
  it('reads the graph back into whole steps, filling in what the API leaves out', async () => {
    http.GET.mockResolvedValue({ data: { data: onTheWire(dto(storedSteps)) }, response: ok });

    const { hook } = mount(() => usePipeline(channel, 'p1'));

    await settle();

    expect(hook.data.value?.triggerCount).toBe(4);
    expect(hook.data.value?.graph.steps).toEqual(storedSteps);
  });

  it('reads a pipeline without a graph as one with no steps', async () => {
    http.GET.mockResolvedValue({
      data: { data: onTheWire({ ...dto([]), graph: null }) },
      response: ok,
    });

    const { hook } = mount(() => usePipeline(channel, 'p1'));

    await settle();

    expect(hook.data.value?.graph).toEqual({ steps: [] });
  });
});

describe('useSavePipeline', () => {
  it('shows the new graph at once, sends it whole and keeps the ids the bot gave new steps', async () => {
    const added = step({ order: 1, action: { type: 'wait', seconds: 3 } });
    let answer!: (value: Answer) => void;

    http.PUT.mockReturnValue(new Promise<Answer>((done) => {
      answer = done;
    }));

    const { queries, hook } = mount(() => useSavePipeline(channel));
    const list = vi.spyOn(queries, 'invalidateQueries');

    queries.setQueryData(pipelineKey(channel, 'p1'), { ...dto(storedSteps), triggerCount: 4 });
    hook.mutate({ id: 'p1', graph: { steps: [...storedSteps, added] } });
    await settle();

    const shown = queries.getQueryData<Pipeline>(pipelineKey(channel, 'p1'));

    expect(shown?.graph.steps.at(-1)).toEqual(added);

    const [path, init] = http.PUT.mock.calls[0]!;

    expect(path).toBe('/api/v{version}/channels/{channelId}/pipelines/{id}');

    expect(init).toEqual({
      params: { path: {
        version: '1',
        channelId: channel,
        id: 'p1',
      } },
      body: { graph: { steps: [...storedSteps, added] } },
    });

    answer({
      data: { data: onTheWire(dto([...storedSteps, { ...added, id: 'new-1' }])) },
      response: ok,
    });

    await settle();
    await settle();

    const saved = queries.getQueryData<Pipeline>(pipelineKey(channel, 'p1'));

    expect(saved?.graph.steps.map((s: PipelineStep) => s.id)).toEqual([
      'if-1',
      'then-1',
      'else-1',
      'new-1',
    ]);

    expect(list).toHaveBeenCalledWith({ queryKey: pipelinesKey(channel) });
  });

  it('puts the old graph back and tells the error inbox when the bot refuses the save', async () => {
    http.PUT.mockResolvedValue({
      error: { message: 'Step 2 has no message.' },
      response: { ok: false, status: 400 },
    });

    const { queries, hook, inbox } = mount(() => useSavePipeline(channel));

    queries.setQueryData(pipelineKey(channel, 'p1'), { ...dto(storedSteps), triggerCount: 4 });
    hook.mutate({ id: 'p1', graph: { steps: [] } });
    await settle();
    await settle();

    expect(queries.getQueryData<Pipeline>(pipelineKey(channel, 'p1'))?.graph.steps).toEqual(storedSteps);

    expect(inbox.items.map((item: { titleKey: string }) => item.titleKey)).toEqual([
      'frame.inbox.saveFailedTitle',
    ]);

    expect(inbox.items[0]?.what).toEqual({ key: 'savePipeline', params: { name: 'p1' } });
  });
});

describe('useCreatePipeline', () => {
  it('makes an empty pipeline by name and refreshes the list', async () => {
    http.POST.mockResolvedValue({ data: { data: onTheWire(dto([])) }, response: ok });

    const { queries, hook } = mount(() => useCreatePipeline(channel));
    const list = vi.spyOn(queries, 'invalidateQueries');
    const made = await hook.mutateAsync({ name: 'Shout' });

    expect(http.POST.mock.calls[0]).toEqual([
      '/api/v{version}/channels/{channelId}/pipelines',
      {
        params: { path: { version: '1', channelId: channel } },
        body: { name: 'Shout', graph: { steps: [] } },
      },
    ]);

    expect(made.id).toBe('p1');
    expect(made.graph.steps).toEqual([]);
    expect(list).toHaveBeenCalledWith({ queryKey: pipelinesKey(channel) });
  });

  it('throws the bot\'s reason when it refuses', async () => {
    http.POST.mockResolvedValue({
      error: { message: 'A pipeline with this name already exists.' },
      response: { ok: false, status: 409 },
    });

    const { hook } = mount(() => useCreatePipeline(channel));

    await expect(hook.mutateAsync({ name: 'Shout' })).rejects.toThrow('A pipeline with this name already exists.');
  });
});

describe('useTestRunPipeline', () => {
  it('runs the saved pipeline with the test variables and returns what each step did', async () => {
    const trace = [{
      stepId: 'if-1',
      stepType: 'if',
      branch: 'then',
    }];

    http.POST.mockResolvedValue({
      data: { data: onTheWire({
        success: true,
        durationMs: '12',
        hostCallCount: 0,
        capturedEffects: [],
        chatOutput: ['rich'],
        log: [],
        trace,
      }) },
      response: ok,
    });

    const { hook } = mount(() => useTestRunPipeline(channel));
    const result = await hook.mutateAsync({ id: 'p1', variables: { 'user.points': '150' } });

    expect(http.POST.mock.calls[0]).toEqual([
      '/api/v{version}/channels/{channelId}/pipelines/{id}/test-run',
      {
        params: { path: {
          version: '1',
          channelId: channel,
          id: 'p1',
        } },
        body: { variables: { 'user.points': '150' } },
      },
    ]);

    expect(result.chatOutput).toEqual(['rich']);
    expect(result.trace).toEqual(trace);
  });
});
