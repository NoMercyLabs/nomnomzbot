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
import { ref, type Ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

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

vi.mock('@/api/pipelines', async (original) => ({
  ...(await original<typeof import('@/api/pipelines')>()),
  usePipeline: () => query(pipeline),
  usePipelineCatalog: () => query(catalog),
  useBlastRadius: () => query(radius),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', schemaEn);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const blank = { template: '<div />' };
  const router = createRouter({
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
        component: blank,
      },
    ],
  });

  await router.push('/app/pipelines/p1');
  mounted = mount(WorkbenchView, { attachTo: document.body, global: { plugins: [i18n, router] } });
  await flushPromises();
}

const cards = (): HTMLElement[] =>
  [...document.body.querySelectorAll<HTMLElement>('[data-step]')];
const card = (id: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-step-id="${id}"]`)!;
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
    expect(inspector.querySelector('[data-inspector-fields]')?.textContent).toContain('hi there');
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

    expect(document.body.querySelector('[data-inspector-fields]')?.textContent).toContain('no id');
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
