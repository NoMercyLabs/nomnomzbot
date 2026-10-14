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
  PipelineStep,
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

type SaveChange = { id: string; graph?: { steps: PipelineStep[] } };

const save = vi.hoisted(() => ({
  mutateAsync: vi.fn<(change: SaveChange) => Promise<unknown>>(),
}));

vi.mock('@/api/pipelines', async (original) => ({
  ...(await original<typeof import('@/api/pipelines')>()),
  usePipeline: () => query(pipeline),
  usePipelineCatalog: () => query(catalog),
  useBlastRadius: () => query(radius),
  useSavePipeline: () => ({ mutateAsync: save.mutateAsync, isPending: ref(false) }),
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
  held.value = ['pipelines:write'];
  save.mutateAsync.mockReset();
  save.mutateAsync.mockResolvedValue({});
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

const cards = (): HTMLElement[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-step]')];
const card = (id: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-step-id="${id}"]`)!;
const fieldInput = (name: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`[data-step-field="${name}"] input, [data-step-field="${name}"] textarea`)!;
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

    expect(document.body.querySelector('[data-workbench-start]')?.textContent).toContain('someone types !roast');
  });

  it('writes the check rule in plain words', async () => {
    await open();

    expect(card('check').querySelector('[data-step-condition]')?.textContent?.trim())
      .toBe('Only if {args.count} is more than 2');
  });

  it('marks a step whose required field is empty as not set up', async () => {
    await open();

    expect(card('pause').querySelector('[data-step-missing]')?.textContent?.trim())
      .toBe('Not set up yet: Seconds is empty.');

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
    const saved = steps.map((item: PipelineStep) =>
      ({ ...item }));

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

const saveButton = (): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>('[data-workbench-save]')!;
const numbers = (): (string | undefined)[] =>
  cards().map((item: HTMLElement) =>
    item.dataset.stepNumber);

describe('changing the steps', () => {
  it('has nothing to undo right after the pipeline loads', async () => {
    await open();

    expect(document.body.querySelector<HTMLButtonElement>('[data-workbench-undo]')?.disabled).toBe(true);

    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await press('[data-workbench-undo]');

    expect(document.body.querySelector<HTMLButtonElement>('[data-workbench-undo]')?.disabled).toBe(true);
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

    expect(sent?.graph?.steps.map((item: PipelineStep) =>
      item.id)).toEqual(['check', 'hi']);
  });

  it('holds the way out while changes are unsaved, until the person leaves without them', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await press('[data-step-remove]');
    await router.push('/app/pipelines');
    await flushPromises();

    expect(router.currentRoute.value.fullPath).toBe('/app/pipelines/p1');

    const leave = [...document.body.querySelectorAll<HTMLButtonElement>('button')]
      .find((button: HTMLButtonElement) =>
        button.textContent?.trim() === 'Leave without saving');

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

    expect([...added.querySelectorAll<HTMLElement>('[data-step-lane]')].map((lane: HTMLElement) =>
      lane.dataset.laneKind)).toEqual(['path', 'path']);
  });

  it('adds a step into the lane the person picked, and says where it goes', async () => {
    await open();
    await press('[data-step-id="check"] [data-lane-kind="no"] [data-lane-add]');

    expect(document.body.querySelector('[data-palette-target]')?.textContent?.trim())
      .toBe('Adds to No of step 1');

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

    const groups = [...document.body.querySelectorAll<HTMLElement>('[data-palette-group]')]
      .map((group: HTMLElement) =>
        group.dataset.paletteGroup);

    expect(groups).toEqual(['blocks', 'pipeline.category.chat', 'pipeline.category.flow']);
  });

  it('changes a setting once the person leaves the field, and Undo brings the old value back', async () => {
    await open();
    await press('[data-step-id="pause"] button');
    await type('seconds', '5');

    expect(card('pause').querySelector('[data-step-missing]')).toBeNull();
    expect(saveButton().disabled).toBe(false);

    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[2]?.action)
      .toEqual({ type: 'wait', seconds: 5 });

    await press('[data-workbench-undo]');

    expect(fieldInput('seconds').value).toBe('');
    expect(card('pause').querySelector('[data-step-missing]')).not.toBeNull();
  });

  it('drops a setting the person empties', async () => {
    await open();
    await press('[data-step-id="hi"] button');
    await type('message', '  ');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[1]?.action).toEqual({ type: 'send_message' });
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

    const names = [...document.body.querySelectorAll<HTMLElement>('[data-step-field]')]
      .map((item: HTMLElement) =>
        item.dataset.stepField);

    expect(names).toEqual(['mode', 'count', 'max_iterations', 'max_loop_runtime_seconds', 'continue_on_error']);
    expect(fieldInput('count').value).toBe('1');

    await type('count', '3');
    await press('[data-workbench-save]');

    expect(save.mutateAsync.mock.calls[0]?.[0].graph?.steps[3]?.block_config)
      .toEqual({ mode: 'repeat', count: 3 });
  });

  it('locks every change for a role that cannot change pipelines', async () => {
    held.value = [];
    await open();
    await press('[data-step-id="hi"] button');

    expect(saveButton().disabled).toBe(true);

    expect(document.body.querySelector<HTMLButtonElement>('[data-step-remove]')?.closest('fieldset')?.disabled)
      .toBe(true);

    expect(document.body.querySelector<HTMLButtonElement>('[data-workbench-add]')?.disabled).toBe(true);
    expect(fieldInput('message').closest('fieldset')?.disabled).toBe(true);
  });
});
