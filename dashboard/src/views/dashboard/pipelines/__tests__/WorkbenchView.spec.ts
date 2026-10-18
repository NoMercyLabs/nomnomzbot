// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, ref, type Ref } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import type {
  Pipeline,
  PipelineBlastRadius,
  PipelineCatalog,
  PipelineOption,
  PipelineOptionList,
  PipelineStep,
  PipelineTestRun,
} from '@/api/pipelines';
import { i18n } from '@/i18n';
import schemaEn from '@/i18n/schema/en.json';

import en from '../i18n/en.json';
import WorkbenchView from '../WorkbenchView.vue';

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
    ...over,
  };
}

// 1 if args.count > 2: yes -> 1.1 say hi, no -> nothing. 2 wait with no seconds set.
const steps: PipelineStep[] = [
  step({
    id: 'check',
    block_kind: 'if',
    action: { type: 'if' },
    condition: {
      type: 'comparison',
      operator: 'gt',
      left: '{args.count}',
      right: '2',
      negate: false,
    },
  }),
  step({
    id: 'hi',
    parent_step_id: 'check',
    branch: 'then',
    action: { type: 'send_message', message: 'hi there' },
  }),
  step({
    id: 'pause',
    order: 1,
    action: { type: 'wait' },
  }),
];

const pipeline = {
  id: 'p1',
  name: 'Roast',
  description: null,
  isEnabled: true,
  graph: { steps },
} as unknown as Pipeline;

const catalog = {
  actions: [
    {
      type: 'send_message',
      category: { key: 'pipeline.category.chat' },
      description: { key: 'pipeline.send_message.description' },
      fields: [
        {
          name: 'message',
          kind: 'text',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
    {
      type: 'speak',
      category: { key: 'pipeline.category.chat' },
      description: { key: 'pipeline.speak.description' },
      fields: [
        {
          name: 'voice',
          kind: 'voice',
          required: false,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
        {
          name: 'channel_id',
          kind: 'discord_channel',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
    {
      type: 'run_pipeline',
      category: { key: 'pipeline.category.flow' },
      description: { key: 'pipeline.run_pipeline.description' },
      fields: [
        {
          name: 'args',
          kind: 'text',
          required: false,
          repeatable: true,
          description: null,
          templated: true,
          declaresVariable: false,
        },
        {
          name: 'named_args',
          kind: 'key_value_map',
          required: false,
          repeatable: false,
          description: null,
          templated: true,
          declaresVariable: false,
        },
      ],
    },
    {
      type: 'wait',
      category: { key: 'pipeline.category.flow' },
      description: { key: 'pipeline.wait.description' },
      fields: [
        {
          name: 'seconds',
          kind: 'number',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
  ],
  conditions: [],
} as unknown as PipelineCatalog;

const radius = {
  commandCount: 1,
  commandNames: ['roast'],
  chatTriggerCount: 0,
  chatTriggerPatterns: [],
  timerCount: 0,
  timerNames: [],
  eventResponseCount: 0,
  eventResponseEventTypes: [],
} as PipelineBlastRadius;

function query<T>(data: T): {
  data: Ref<T>;
  isPending: Ref<boolean>;
  isError: Ref<boolean>;
  refetch: () => void;
} {
  return {
    data: ref(data) as Ref<T>,
    isPending: ref(false),
    isError: ref(false),
    refetch: vi.fn<() => void>(),
  };
}

const held = ref<string[]>([]);

type SaveChange = {
  id: string;
  graph?: { steps: PipelineStep[] };
  isEnabled?: boolean;
};

const save = vi.hoisted(() => ({
  mutateAsync: vi.fn<(change: SaveChange) => Promise<unknown>>(),
  mutate: vi.fn<(change: SaveChange, options: { onSuccess: () => void }) => void>(),
}));

const removing = vi.hoisted(() => ({
  mutateAsync: vi.fn<(id: string) => Promise<void>>(),
}));

type TestRunAsk = {
  id: string;
  variables: Record<string, string>;
  graph: { steps: PipelineStep[] };
};

const testing = vi.hoisted(() => ({
  mutateAsync: vi.fn<(ask: TestRunAsk) => Promise<PipelineTestRun>>(),
}));

// The check said yes, so "hi" ran inside its lane; the wait step after it was never reached.
const ran = {
  success: true,
  durationMs: 12,
  hostCallCount: 0,
  capturedEffects: [],
  chatOutput: ['hi there'],
  log: [],
  timeline: [],
  trace: [
    {
      stepId: 'check',
      stepType: 'if',
      branch: 'then',
      variableChanges: [],
    },
    {
      stepId: 'hi',
      stepType: 'send_message',
      variableChanges: [],
      output: 'Would say: hi there',
    },
  ],
} as unknown as PipelineTestRun;

vi.mock('@/api/pipelines', async (original) => ({
  ...(await original<typeof import('@/api/pipelines')>()),
  usePipeline: () => query(pipeline),
  usePipelineCatalog: () => query(catalog),
  useBlastRadius: () => query(radius),
  useSavePipeline: () => ({
    mutateAsync: save.mutateAsync,
    mutate: save.mutate,
    isPending: ref(false),
  }),
  useDeletePipeline: () => ({ mutateAsync: removing.mutateAsync, isPending: ref(false) }),
  useTestRunPipeline: () => ({ mutateAsync: testing.mutateAsync, isPending: ref(false) }),
  usePipelineOptions: (_channel: unknown, kind: () => string, search: Ref<string>) => ({
    ...query(null),
    data: computed(() =>
      kind() === 'voice'
        ? botSearch(search.value)
        : discordDown),
    isFetching: ref(false),
  }),
  // The bot finds the one item a saved value names, also beyond its first page.
  usePipelineOption: (_channel: unknown, kind: () => string, value: () => string | null) => ({
    ...query(null),
    data: computed(() =>
      kind() === 'voice'
        ? (botSearch('')
            .items.concat(ava)
            .find((option: PipelineOption) => option.value === value()) ?? null)
        : null,
    ),
  }),
}));

// The bot lists its first page and searches the rest: Ava is only on a later page.
function botSearch(search: string): PipelineOptionList {
  const term = search.toLowerCase();

  if (term === '')
    return voices;

  const items = [...voices.items, ava].filter((option: PipelineOption) =>
    option.label.toLowerCase().includes(term),
  );

  return {
    ...voices,
    items,
    totalCount: items.length,
  };
}

const voices: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: 'guy',
      label: 'Guy',
      secondaryText: 'en-US',
      state: 0,
    },
    {
      value: 'jenny',
      label: 'Jenny',
      reason: 'Needs the voice pack',
      state: 1,
    },
  ],
  totalCount: 2,
};
const ava: PipelineOption = {
  value: 'ava',
  label: 'Ava',
  state: 0,
};
const discordDown: PipelineOptionList = {
  sourceAvailable: false,
  unavailableReason: 'Discord is not connected.',
  items: [],
  totalCount: 0,
};

// Two stored runs of p1, newest first; the failed one logs its steps out of order, as the bot may send them.
const stored = vi.hoisted(() => ({
  asked: [] as (string | null)[],
  runs: [
    {
      id: 7,
      pipelineId: 'p1',
      triggerKind: 'pipeline',
      status: 'failed',
      hostCallCount: 2,
      durationMs: 1250,
      errorMessage: 'Twitch refused the message',
      startedAt: '2026-10-14T09:58:00Z',
      completedAt: '2026-10-14T09:58:01Z',
    },
    {
      id: 6,
      pipelineId: 'p1',
      triggerKind: 'test_run',
      status: 'success',
      hostCallCount: 1,
      durationMs: 340,
      errorMessage: null,
      startedAt: '2026-10-13T10:00:00Z',
      completedAt: '2026-10-13T10:00:00Z',
    },
  ],
  steps: [
    {
      stepIndex: 1,
      actionType: 'send_message',
      succeeded: false,
      durationMs: 900,
      errorMessage: 'Twitch refused the message',
    },
    {
      stepIndex: 0,
      actionType: 'wait',
      succeeded: true,
      durationMs: 300,
      errorMessage: null,
    },
  ],
}));

vi.mock('@/api/pipelineRuns', () => ({
  usePipelineRuns: (_channel: unknown, pipelineId: () => string | null) => {
    stored.asked.push(pipelineId());

    return {
      data: ref({
        pages: [
          {
            runs: stored.runs,
            hasMore: false,
            nextPage: null,
          },
        ],
      }),
      isPending: ref(false),
      isError: ref(false),
      hasNextPage: ref(false),
      isFetchingNextPage: ref(false),
      refetch: vi.fn<() => void>(),
      fetchNextPage: vi.fn<() => void>(),
    };
  },
  usePipelineRun: (_channel: unknown, id: () => string | null) => ({
    data: computed(() =>
      id() === '7'
        ? { ...stored.runs[0], stepLogs: stored.steps }
        : undefined,
    ),
    isPending: ref(false),
    isError: ref(false),
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: computed(() => true),
    can: (key: string) => held.value.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', schemaEn);
  held.value = ['pipelines:read', 'pipelines:write'];
  save.mutateAsync.mockReset();
  save.mutateAsync.mockResolvedValue({});
  save.mutate.mockReset();
  removing.mutateAsync.mockReset();
  removing.mutateAsync.mockResolvedValue();
  testing.mutateAsync.mockReset();
  testing.mutateAsync.mockResolvedValue(ran);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

let router: Router;

async function open(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/pipelines',
        name: 'dashboard-pipelines',
        component: blank,
      },
      {
        path: '/app/pipelines/:id',
        name: 'dashboard-pipeline',
        component: WorkbenchView,
      },
    ],
  });

  await router.push('/app/pipelines/p1');
  // Routed as in the app: the leave guard only binds inside a RouterView.
  mounted = mount(RouterView, { attachTo: document.body, global: { plugins: [i18n, router] } });
  await flushPromises();
}

const cards = (): HTMLElement[] => [...document.body.querySelectorAll<HTMLElement>('[data-step]')];
const card = (id: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-step-id="${id}"]`)!;
const fieldInput = (name: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(
    `[data-step-field="${name}"] input, [data-step-field="${name}"] textarea`,
  )!;
const titleOf = (id: string): string | undefined =>
  card(id).querySelector('[data-step-title]')?.textContent?.trim();

describe('the workbench canvas', () => {
  it('numbers the steps as the engine walks them, with the check lanes inside', async () => {
    await open();

    expect(cards().map((item: HTMLElement) => item.dataset.stepNumber)).toEqual(['1', '1.1', '2']);
    expect(titleOf('check')).toBe('If, otherwise');
    expect(titleOf('hi')).toBe('Send message');

    const lanes = [...card('check').querySelectorAll<HTMLElement>('[data-step-lane]')];

    expect(lanes.map((lane: HTMLElement) => lane.dataset.laneKind)).toEqual(['yes', 'no']);
    expect(lanes[0]?.querySelector('[data-step-id="hi"]')).not.toBeNull();
    expect(lanes[1]?.textContent).toContain('Nothing extra. Goes on to the next step.');
  });

  it('says what starts the pipeline from its blast radius', async () => {
    await open();

    expect(document.body.querySelector('[data-workbench-start]')?.textContent).toContain(
      'someone types !roast',
    );
  });

  it('writes the check rule in plain words', async () => {
    await open();

    expect(card('check').querySelector('[data-step-condition]')?.textContent?.trim()).toBe(
      'Only if {args.count} is more than 2',
    );
  });

  it('offers a way back to the pipelines list', async () => {
    await open();
    const back = document.body.querySelector<HTMLButtonElement>('[data-slot="back-link"]');

    expect(back?.textContent).toContain('Back to the list');
    back!.click();
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('dashboard-pipelines');
  });

  it('marks a step whose required field is empty as not set up', async () => {
    await open();

    expect(card('pause').querySelector('[data-step-missing]')?.textContent?.trim()).toBe(
      'Not set up yet: Seconds is empty.',
    );

    expect(card('hi').querySelector('[data-step-missing]')).toBeNull();
  });
});

describe('the step inspector', () => {
  it('shows the picked step with its saved values and the field help', async () => {
    await open();
    card('hi').querySelector<HTMLButtonElement>('button')!.click();
    await flushPromises();

    const inspector = document.body.querySelector<HTMLElement>('[data-step-inspector]')!;

    expect(inspector.textContent).toContain('Step 1.1');
    expect(fieldInput('message').value).toBe('hi there');
    expect(card('hi').querySelector('button')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('opens a step the bot saved without an id, as it saves every plain step', async () => {
    const saved = steps.map((item: PipelineStep) => ({ ...item }));

    steps.splice(0, steps.length, step({ action: { type: 'send_message', message: 'no id' } }));
    await open();
    cards()[0]!.querySelector<HTMLButtonElement>('button')!.click();
    await flushPromises();
    steps.splice(0, steps.length, ...saved);

    expect(fieldInput('message').value).toBe('no id');
  });

  it('closes when the person picks the same step again', async () => {
    await open();
    card('hi').querySelector<HTMLButtonElement>('button')!.click();
    await flushPromises();
    card('hi').querySelector<HTMLButtonElement>('button')!.click();
    await flushPromises();

    expect(document.body.querySelector('[data-step-inspector]')).toBeNull();
  });
});

async function press(selector: string): Promise<void> {
  document.body.querySelector<HTMLButtonElement>(selector)!.click();
  await flushPromises();
}

// Types into a field and leaves it, as a person does.
async function type(name: string, value: string): Promise<void> {
  const input = fieldInput(name);

  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('change'));
  await flushPromises();
}

// Types into one input found by selector and leaves it.
async function typeInto(selector: string, value: string): Promise<void> {
  const input = document.body.querySelector<HTMLInputElement>(selector)!;

  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('change'));
  await flushPromises();
}

// Searches in the open picker and waits past its pause, so the bot is asked.
async function find(text: string): Promise<void> {
  const search = document.body.querySelector<HTMLInputElement>(
    '[data-slot="popover-content"] input',
  )!;

  search.value = text;
  search.dispatchEvent(new Event('input'));

  await new Promise<void>((done: () => void) => setTimeout(done, 300));

  await flushPromises();
}

const saveButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-workbench-save]')!;
const numbers = (): (string | undefined)[] =>
  cards().map((item: HTMLElement) => item.dataset.stepNumber);

describe('changing the steps', () => {
  it('has nothing to undo right after the pipeline loads', async () => {
    await open();

    expect(document.body.querySelector<HTMLButtonElement>('[data-workbench-undo]')?.disabled).toBe(
      true,
    );

    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await press('[data-workbench-undo]');

    expect(document.body.querySelector<HTMLButtonElement>('[data-workbench-undo]')?.disabled).toBe(
      true,
    );

    expect(numbers()).toEqual(['1', '1.1', '2']);
  });

  it('removes the picked step and brings it back on Undo', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');

    expect(document.body.querySelector('[data-step-id="pause"]')).toBeNull();
    expect(document.body.querySelector('[data-step-inspector]')).toBeNull();

    await press('[data-workbench-undo]');

    expect(numbers()).toEqual(['1', '1.1', '2']);
  });

  it('duplicates the picked step right after it, inside its lane', async () => {
    await open();
    await press('[data-step-id="hi"] button');
    await press('[data-step-duplicate]');

    expect(numbers()).toEqual(['1', '1.1', '1.2', '2']);
  });

  it('saves the changed steps as the whole graph, and only once something changed', async () => {
    await open();

    expect(saveButton().disabled).toBe(true);

    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await press('[data-workbench-save]');

    const sent = save.mutateAsync.mock.calls[0]?.[0];

    expect(sent?.id).toBe('p1');

    expect(sent?.graph?.steps.map((item: PipelineStep) => item.id)).toEqual(['check', 'hi']);
  });

  it('holds the way out while changes are unsaved, until the person leaves without them', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await router.push('/app/pipelines');
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines/p1');

    const leave = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button: HTMLButtonElement) => button.textContent?.trim() === 'Leave without saving',
    );

    leave!.click();
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines');
  });

  it('adds a picked action from the palette at the end and opens it to set it up', async () => {
    await open();
    await press('[data-workbench-add]');

    const item = document.body.querySelector<HTMLElement>('[data-palette-action="wait"]')!;

    item.click();
    await flushPromises();

    expect(numbers()).toEqual(['1', '1.1', '2', '3']);

    const inspector = document.body.querySelector<HTMLElement>('[data-step-inspector]')!;

    expect(inspector.textContent).toContain('Step 3');
    expect(inspector.textContent).toContain('Wait');
    expect(saveButton().disabled).toBe(false);
  });

  it('adds a check from the palette with the lanes it needs', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-block="random_branch"]');

    const added = cards().at(-1)!;

    expect(added.dataset.stepNumber).toBe('3');

    expect(
      [...added.querySelectorAll<HTMLElement>('[data-step-lane]')].map(
        (lane: HTMLElement) => lane.dataset.laneKind,
      ),
    ).toEqual(['path', 'path']);
  });

  it('adds a step into the lane the person picked, and says where it goes', async () => {
    await open();
    await press('[data-step-id="check"] [data-lane-kind="no"] [data-lane-add]');

    expect(document.body.querySelector('[data-palette-target]')?.textContent?.trim()).toBe(
      'Adds to No of step 1',
    );

    await press('[data-palette-action="wait"]');

    const lanes = [...card('check').querySelectorAll<HTMLElement>('[data-step-lane]')];
    const added = lanes[1]?.querySelector<HTMLElement>('[data-step]');

    expect(added?.dataset.stepNumber).toBe('1.1');
    expect(added?.querySelector('[data-step-title]')?.textContent?.trim()).toBe('Wait');
    expect(numbers()).toEqual(['1', '1.1', '1.1', '2']);
  });

  it('adds at the end again once the person lets go of the lane', async () => {
    await open();
    await press('[data-step-id="check"] [data-lane-kind="no"] [data-lane-add]');
    await press('[data-palette-target] button');

    expect(document.body.querySelector('[data-palette-target]')).toBeNull();

    await press('[data-palette-action="wait"]');

    expect(numbers()).toEqual(['1', '1.1', '2', '3']);
  });

  it('groups the palette actions under their category words', async () => {
    await open();
    await press('[data-workbench-add]');

    const groups = [...document.body.querySelectorAll<HTMLElement>('[data-palette-group]')].map(
      (group: HTMLElement) => group.dataset.paletteGroup,
    );

    expect(groups).toEqual(['blocks', 'pipeline.category.chat', 'pipeline.category.flow']);
  });

  it('changes a setting once the person leaves the field, and Undo brings the old value back', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await type('seconds', '5');

    expect(card('pause').querySelector('[data-step-missing]')).toBeNull();
    expect(saveButton().disabled).toBe(false);

    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[2]?.action).toEqual({
      type: 'wait',
      seconds: 5,
    });

    await press('[data-workbench-undo]');

    expect(fieldInput('seconds').value).toBe('');
    expect(card('pause').querySelector('[data-step-missing]')).not.toBeNull();
  });

  it('drops a setting the person empties', async () => {
    await open();
    await press('[data-step-id="hi"] button');
    await type('message', '  ');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[1]?.action).toEqual({
      type: 'send_message',
    });
  });

  it('lets a step keep the pipeline going when it fails', async () => {
    await open();
    await press('[data-step-id="hi"] button');
    await press('[data-step-field="continue_on_error"] button[role="switch"]');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[1]?.continue_on_error).toBe(true);
  });

  it('gives a new repeat its own settings, starting at once', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-block="loop"]');

    const names = [...document.body.querySelectorAll<HTMLElement>('[data-step-field]')].map(
      (item: HTMLElement) => item.dataset.stepField,
    );

    expect(names).toEqual([
      'mode',
      'count',
      'max_iterations',
      'max_loop_runtime_seconds',
      'continue_on_error',
    ]);

    expect(fieldInput('count').value).toBe('1');

    await type('count', '3');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.block_config).toEqual({
      mode: 'repeat',
      count: 3,
    });
  });

  it('builds a list setting item by item, keeps a JSON object whole, and removes one item', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="run_pipeline"]');
    await press('[data-step-field="args"] [data-list-add]');
    await press('[data-step-field="args"] [data-list-add]');
    await press('[data-step-field="args"] [data-list-add]');
    await typeInto('[data-step-field="args"] [data-list-item]:nth-child(1) input', '{user}');
    await typeInto('[data-step-field="args"] [data-list-item]:nth-child(3) input', '{"level": 2}');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'run_pipeline',
      args: ['{user}', { level: 2 }],
    });

    await press('[data-step-field="args"] [data-list-remove]');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[1]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'run_pipeline',
      args: [{ level: 2 }],
    });
  });

  it('saves names with values, and holds a name used twice until it is changed', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="run_pipeline"]');
    await press('[data-step-field="named_args"] [data-map-add]');
    await typeInto('[data-map-row]:nth-child(1) [data-map-value]', '{user}');
    await typeInto('[data-map-row]:nth-child(1) [data-map-name]', 'who');
    await press('[data-step-field="named_args"] [data-map-add]');
    await typeInto('[data-map-row]:nth-child(2) [data-map-name]', 'who');

    expect(document.body.querySelector('[data-map-twice]')?.textContent).toContain('who');

    await typeInto('[data-map-row]:nth-child(2) [data-map-value]', 'loud');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'run_pipeline',
      named_args: { who: '{user}' },
    });

    await typeInto('[data-map-row]:nth-child(2) [data-map-name]', 'mood');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[1]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'run_pipeline',
      named_args: { who: '{user}', mood: 'loud' },
    });
  });

  it('picks a voice the bot lists, and says why one it cannot use is off', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="speak"]');
    await press('[data-step-field="voice"] button');

    const jenny = document.body.querySelector<HTMLElement>('[data-picker-option="jenny"]')!;

    expect(jenny.hasAttribute('data-disabled')).toBe(true);
    expect(jenny.textContent).toContain('Needs the voice pack');

    await press('[data-picker-option="guy"]');

    expect(document.body.querySelector('[data-step-field="voice"] button')?.textContent).toContain(
      'Guy',
    );

    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'speak',
      voice: 'guy',
    });
  });

  it('shows what the bot finds beyond its first page, and says so only when it finds nothing', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="speak"]');
    await press('[data-step-field="voice"] button');
    await find('Ava');

    const popover = (): string =>
      document.body.querySelector('[data-slot="popover-content"]')?.textContent ?? '';

    expect(document.body.querySelector('[data-picker-option="ava"]')).not.toBeNull();
    expect(popover()).not.toContain(en.dashboard.pipelines.workbench.picker.noMatch);

    await find('zzz');

    expect(popover()).toContain(en.dashboard.pipelines.workbench.picker.noMatch);
  });

  it('names a chosen item from beyond the first page, not its id', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="speak"]');
    await press('[data-step-field="voice"] button');
    await find('Ava');
    await press('[data-picker-option="ava"]');

    // The search clears on a choice, so the list falls back to the first page, where Ava is not.
    await new Promise((resolve) => setTimeout(resolve, 300));

    await flushPromises();

    expect(
      document.body.querySelector('[data-step-field="voice"] button')?.textContent?.trim(),
    ).toBe('Ava');
  });

  it('keeps typed words as the setting, so it can fill in from the run', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="speak"]');
    await press('[data-step-field="voice"] button');

    const search = document.body.querySelector<HTMLInputElement>(
      '[data-slot="popover-content"] input',
    )!;

    search.value = '{user.voice}';
    search.dispatchEvent(new Event('input'));
    await flushPromises();
    await press('[data-picker-typed]');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.action).toEqual({
      type: 'speak',
      voice: '{user.voice}',
    });
  });

  it('says why a list the bot cannot reach is empty', async () => {
    await open();
    await press('[data-workbench-add]');
    await press('[data-palette-action="speak"]');
    await press('[data-step-field="channel_id"] button');

    expect(document.body.querySelector('[data-picker-unavailable]')?.textContent?.trim()).toBe(
      'Discord is not connected.',
    );
  });

  it('hides every change for a role that cannot change pipelines', async () => {
    held.value = ['pipelines:read'];
    await open();
    await press('[data-step-id="hi"] button');

    expect(document.body.querySelector('[data-workbench-save]')).toBeNull();
    expect(document.body.querySelector('[data-step-remove]')).toBeNull();
    expect(document.body.querySelector('[data-workbench-add]')).toBeNull();
    expect(fieldInput('message').closest('fieldset')?.disabled).toBe(true);
  });
});

const mark = (id: string, part: string): string | undefined =>
  card(id).querySelector<HTMLElement>(`:scope > button [data-run-${part}]`)?.textContent?.trim();
const stateOf = (id: string): string | undefined =>
  card(id).querySelector<HTMLElement>(':scope > button [data-run-state]')?.dataset.state;

describe('the pipeline actions in the bar', () => {
  const powerSwitch = (): HTMLButtonElement =>
    document.body.querySelector<HTMLButtonElement>('[data-workbench-power] [role="switch"]')!;

  it('turns the pipeline off without sending the steps', async () => {
    await open();

    expect(document.body.querySelector('[data-workbench-power]')!.textContent).toContain('Enabled');
    expect(powerSwitch().getAttribute('aria-checked')).toBe('true');

    powerSwitch().click();
    await flushPromises();

    expect(save.mutate).toHaveBeenCalledTimes(1);
    expect(save.mutate.mock.calls[0]![0]).toEqual({ id: 'p1', isEnabled: false });
  });

  it('deletes only after the confirm that names what starts it, then leaves despite unsaved edits', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await press('[data-workbench-delete]');

    const dialog = document.body.querySelector<HTMLElement>('[role="alertdialog"]')!;

    expect(dialog.textContent).toContain('Delete Roast?');
    expect(dialog.textContent).toContain('When someone types !roast, nothing runs anymore.');
    expect(removing.mutateAsync).not.toHaveBeenCalled();

    await press('[data-confirm-action]');

    expect(removing.mutateAsync).toHaveBeenCalledWith('p1');
    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines');
  });

  it('shows only the state to a role that cannot change pipelines', async () => {
    held.value = ['pipelines:read'];
    await open();

    expect(document.body.querySelector('[data-workbench-power]')).toBeNull();

    expect(document.body.querySelector('[data-workbench-power-state]')?.textContent?.trim()).toBe(
      'On',
    );

    expect(document.body.querySelector('[data-workbench-delete]')).toBeNull();
  });
});

describe('the Runs view', () => {
  async function openRuns(): Promise<void> {
    await open();

    await press('[data-workbench-runs]');
  }

  const runRows = (): HTMLElement[] => [
    ...document.body.querySelectorAll<HTMLElement>('[data-run]'),
  ];

  it('opens beside the steps and lists the stored runs of this pipeline, with outcome, age, length and error', async () => {
    stored.asked.length = 0;
    await openRuns();

    expect(router.currentRoute.value.query.view).toBe('runs');
    expect(document.body.querySelector('[data-workbench-canvas]')).not.toBeNull();
    expect(stored.asked).toContain('p1');

    const rows = runRows().map((row: HTMLElement) => row.textContent?.replace(/\s+/g, ' ').trim());

    expect(rows[0]).toContain('Failed');
    expect(rows[0]).toContain('took 1.3 sec');
    expect(rows[0]).toContain('Twitch refused the message');
    expect(rows[1]).toContain('Done');
    expect(rows[1]).toContain('took 340 ms');
  });

  it('marks a test run, and only a test run', async () => {
    await openRuns();

    const marked = runRows().map(
      (row: HTMLElement) => row.querySelector('[data-run-test]')?.textContent?.trim() ?? null,
    );

    expect(marked).toEqual([null, 'Test run']);
  });

  it('opens a run to say what each step did, in the order they ran', async () => {
    await openRuns();

    runRows()[0]!.querySelector<HTMLElement>('button')!.click();
    await flushPromises();

    const steps = [...document.body.querySelectorAll<HTMLElement>('[data-run-step]')].map(
      (step: HTMLElement) => step.textContent?.replace(/\s+/g, ' ').trim(),
    );

    expect(steps).toHaveLength(2);
    expect(steps[0]).toMatch(/^Step 1: Wait\s*300 ms\s*Done$/);

    expect(steps[1]).toMatch(
      /^Step 2: Send message\s*900 ms\s*Failed\s*Twitch refused the message$/,
    );
  });
});

describe('a test run', () => {
  it('runs the draft on the canvas and marks what each step did', async () => {
    await open();
    await press('[data-workbench-test]');

    const ask = testing.mutateAsync.mock.calls[0]?.[0];

    expect(ask?.id).toBe('p1');
    expect(ask?.variables).toEqual({});

    expect(ask?.graph.steps.map((item: PipelineStep) => item.id)).toEqual(['check', 'hi', 'pause']);

    expect([stateOf('check'), stateOf('hi'), stateOf('pause')]).toEqual([
      'done',
      'done',
      'skipped',
    ]);

    expect(mark('check', 'taken')).toBe('This run: Yes');
    expect(mark('hi', 'output')).toBe('Would say: hi there');
    expect(card('pause').querySelector(':scope > button')?.className).toContain('border-dashed');

    expect(document.body.querySelector('[data-run-summary]')?.textContent?.trim()).toContain(
      'Test run 1: 2 steps ran.',
    );
  });

  it('says a failed step failed, with the reason', async () => {
    testing.mutateAsync.mockResolvedValue({
      ...ran,
      success: false,
      trace: [
        {
          stepId: 'pause',
          stepType: 'wait',
          variableChanges: [],
          error: 'No seconds set.',
        },
      ],
    } as unknown as PipelineTestRun);

    await open();
    await press('[data-workbench-test]');

    expect(stateOf('pause')).toBe('failed');
    expect(mark('pause', 'error')).toBe('No seconds set.');
  });

  it('clears the marks once the draft changes, or on Clear test run', async () => {
    await open();
    await press('[data-workbench-test]');
    await press('[data-workbench-test]');

    expect(stateOf('hi')).toBeUndefined();
    expect(document.body.querySelector('[data-run-summary]')).toBeNull();

    await press('[data-workbench-test]');
    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');

    expect(stateOf('hi')).toBeUndefined();
  });

  it('counts each run again in the summary', async () => {
    await open();
    await press('[data-workbench-test]');
    await press('[data-run-again]');

    expect(testing.mutateAsync).toHaveBeenCalledTimes(2);

    expect(document.body.querySelector('[data-run-summary]')?.textContent?.trim()).toContain(
      'Test run 2: 2 steps ran.',
    );
  });
});
