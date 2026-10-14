// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { useSaveErrorsStore } from '@/stores/saveErrors';

import QuickActionsPanel from '../components/QuickActionsPanel.vue';
import en from '../i18n/en.json';

type Call = {
  method: string;
  path: string;
  body?: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let predictionList: Row[] = [];
let startFails = false;
let patchFails = false;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (): Answer => ({
  error: { status: 'error' },
  response: { status: 500, ok: false },
});

const runningPrediction = (id: string, status: string): Row => ({
  id,
  title: 'Will it build?',
  status,
  predictionWindow: 300,
  winningOutcomeId: null,
  createdAt: new Date().toISOString(),
  endedAt: null,
  lockedAt: status === 'LOCKED'
    ? new Date().toISOString()
    : null,
  outcomes: [
    {
      id: `${id}-a`,
      title: 'Yes',
      users: 3,
      channelPoints: 750,
      topPredictors: [],
      color: 'BLUE',
    },
    {
      id: `${id}-b`,
      title: 'No',
      users: 1,
      channelPoints: 250,
      topPredictors: [],
      color: 'PINK',
    },
  ],
});

const startedPrediction = (body: Row): Row => ({
  ...runningPrediction('server-1', 'ACTIVE'),
  title: body.title,
  predictionWindow: body.predictionWindowSeconds,
  outcomes: (body.outcomes as string[]).map((title, index) => ({
    id: `server-1-${index}`,
    title,
    users: 0,
    channelPoints: 0,
    topPredictors: [],
    color: 'BLUE',
  })),
});

function answer(method: string, path: string): Answer {
  calls.push({ method, path });

  if (path.endsWith('/live-ops/predictions'))
    return ok(predictionList);

  if (path.endsWith('/ads/schedule'))
    return ok(null);

  if (path.endsWith('/music/queue'))
    return ok({ nowPlaying: null, queue: [] });

  return ok([]);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer('GET', path),
    PATCH: async (
      path: string,
      options?: { params?: { path?: { predictionId?: string } }; body?: Row },
    ) => {
      calls.push({
        method: 'PATCH',
        path,
        body: options?.body,
      });

      if (patchFails)
        return refused();

      predictionList = predictionList.map((prediction) =>
        prediction.id === options?.params?.path?.predictionId
          ? {
              ...prediction,
              status: options?.body?.status,
              winningOutcomeId: options?.body?.winningOutcomeId ?? null,
            }
          : prediction,
      );

      return ok(null);
    },
    POST: async (path: string, options?: { body?: Row }) => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      if (startFails)
        return refused();

      predictionList = [startedPrediction(options?.body as Row), ...predictionList];

      return { data: { data: predictionList[0] }, response: { status: 201, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  predictionList = [];
  startFails = false;
  patchFails = false;
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  const blank = { template: '<div />' };
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/app',
      name: 'dashboard-home',
      component: blank,
    }],
  });

  await router.push('/app');

  mounted = mount(QuickActionsPanel, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const row = (): HTMLElement =>
  document.body.querySelector<HTMLElement>('[data-quick-row="poll"]') as HTMLElement;
const dialog = (): HTMLElement =>
  document.body.querySelector<HTMLElement>('[role="dialog"]') as HTMLElement;
const confirmBox = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[role="alertdialog"]');
const field = (name: string): HTMLInputElement =>
  dialog().querySelector<HTMLInputElement>(`[data-prediction-field="${name}"]`) as HTMLInputElement;
const outcomeInput = (index: number): HTMLInputElement =>
  dialog().querySelector<HTMLInputElement>(`[data-prediction-outcome="${index}"]`) as HTMLInputElement;
const result = (title: string): HTMLElement =>
  dialog().querySelector<HTMLElement>(`[data-prediction-result="${title}"]`) as HTMLElement;
const buttonIn = (root: HTMLElement, label: string): HTMLElement =>
  [...root.querySelectorAll<HTMLElement>('button, a')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLElement;
const patches = (): Call[] => calls.filter((call) => call.method === 'PATCH');
const posts = (): Call[] => calls.filter((call) => call.method === 'POST');

async function click(target: HTMLElement): Promise<void> {
  target.click();
  await settle();
}

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function pressDialog(label: string): Promise<void> {
  const target = buttonIn(dialog(), label);

  await click(target);
}

async function pressRow(label: string): Promise<void> {
  const target = buttonIn(row(), label);

  await click(target);
}

async function openRunning(status: string): Promise<void> {
  predictionList = [runningPrediction('p1', status)];
  await open();
  await pressRow('Open prediction');
}

async function openForm(): Promise<void> {
  await open();
  await pressRow('Start a prediction');
}

async function fillBasics(): Promise<void> {
  await type(field('title'), '  Will it build?  ');
  await type(outcomeInput(0), 'Yes');
  await type(outcomeInput(1), 'No');
}

describe('prediction dialog', () => {
  it('offers Start a prediction beside Start a poll when nothing runs', async () => {
    await open();

    const labels = [...row().querySelectorAll('button, a')].map((b) => b.textContent?.trim());

    expect(labels).toEqual(['Start a poll', 'Start a prediction']);
  });

  it('starts a prediction: one POST with the title, the trimmed outcomes and the window in seconds', async () => {
    await openForm();
    await fillBasics();
    await type(field('minutes'), '3');
    await pressDialog('Start prediction');

    expect(posts()).toEqual([
      {
        method: 'POST',
        path: expect.stringContaining('/live-ops/predictions'),
        body: {
          title: 'Will it build?',
          outcomes: ['Yes', 'No'],
          predictionWindowSeconds: 180,
        },
      },
    ]);
  });

  it('adds outcomes and sends them; allows at most ten', async () => {
    await openForm();
    await fillBasics();

    for (let added = 0; added < 10; added += 1) {
      const add = buttonIn(dialog(), 'Add outcome');

      if (add) {
        await click(add);
      }
    }

    expect(dialog().querySelectorAll('[data-prediction-outcome]')).toHaveLength(10);
    expect(buttonIn(dialog(), 'Add outcome')).toBeUndefined();

    await type(outcomeInput(2), 'Maybe');
    await pressDialog('Start prediction');

    const body = posts()[0]!.body as { outcomes: string[] };

    expect(body.outcomes).toEqual(['Yes', 'No', 'Maybe']);
  });

  it('blocks Start with fewer than two outcomes and sends nothing', async () => {
    await openForm();
    await type(field('title'), 'Will it build?');
    await type(outcomeInput(0), 'Yes');

    const start = buttonIn(dialog(), 'Start prediction') as HTMLButtonElement;

    expect(start.disabled).toBe(true);
    expect(dialog().textContent).toContain('Give at least 2 outcomes.');

    await click(start);

    expect(posts()).toHaveLength(0);
  });

  it('blocks Start without a title', async () => {
    await openForm();
    await type(outcomeInput(0), 'Yes');
    await type(outcomeInput(1), 'No');

    expect((buttonIn(dialog(), 'Start prediction') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows the new prediction at once: the row names it and the dialog turns into its outcomes', async () => {
    await openForm();
    await fillBasics();
    await pressDialog('Start prediction');

    expect(row().textContent).toContain('Will it build?');
    expect(row().textContent).toContain('Prediction');
    expect(result('Yes').textContent).toContain('0 points');
    expect(buttonIn(dialog(), 'Lock prediction')).toBeDefined();
    expect(buttonIn(dialog(), 'Start prediction')).toBeUndefined();
  });

  it('reports a refused start and puts the row back to idle', async () => {
    startFails = true;
    await openForm();
    await fillBasics();
    await pressDialog('Start prediction');

    expect(useSaveErrorsStore().items).toHaveLength(1);
    expect(row().textContent).toContain('No poll or prediction running');
    expect(outcomeInput(0).value).toBe('Yes');
  });

  it('shows each outcome with its points, its viewers and its share', async () => {
    await openRunning('ACTIVE');

    expect(result('Yes').textContent).toContain('750 points');
    expect(result('Yes').textContent).toContain('3 viewers');
    expect(result('Yes').textContent).toContain('75%');
    expect(result('No').textContent).toContain('250 points');
    expect(result('No').textContent).toContain('1 viewer');
    expect(result('No').textContent).toContain('25%');
  });

  it('locks an open prediction: one PATCH with LOCKED, then Resolve takes the place of Lock', async () => {
    await openRunning('ACTIVE');
    await pressDialog('Lock prediction');

    expect(patches()).toEqual([
      {
        method: 'PATCH',
        path: expect.stringContaining('/live-ops/predictions/{predictionId}/end'),
        body: { status: 'LOCKED' },
      },
    ]);

    expect(buttonIn(dialog(), 'Lock prediction')).toBeUndefined();
    expect(buttonIn(dialog(), 'Resolve prediction')).toBeDefined();
    expect(row().textContent).toContain('Locked');
  });

  it('keeps the prediction open and reports a refused lock', async () => {
    patchFails = true;
    await openRunning('ACTIVE');
    await pressDialog('Lock prediction');

    expect(useSaveErrorsStore().items).toHaveLength(1);
    expect(buttonIn(dialog(), 'Lock prediction')).toBeDefined();
    expect(row().textContent).not.toContain('Locked');
  });

  it('resolves only after a winner is picked, and sends the winning outcome id', async () => {
    await openRunning('LOCKED');

    const resolve = buttonIn(dialog(), 'Resolve prediction') as HTMLButtonElement;

    expect(resolve.disabled).toBe(true);

    await click(dialog().querySelector<HTMLElement>('[data-prediction-pick="p1-b"]') as HTMLElement);

    expect((buttonIn(dialog(), 'Resolve prediction') as HTMLButtonElement).disabled).toBe(false);

    await pressDialog('Resolve prediction');

    expect(patches()).toEqual([
      {
        method: 'PATCH',
        path: expect.stringContaining('/live-ops/predictions/{predictionId}/end'),
        body: { status: 'RESOLVED', winningOutcomeId: 'p1-b' },
      },
    ]);

    expect(row().textContent).toContain('No poll or prediction running');
  });

  it('keeps a locked prediction and reports a refused resolve', async () => {
    patchFails = true;
    await openRunning('LOCKED');
    await click(dialog().querySelector<HTMLElement>('[data-prediction-pick="p1-a"]') as HTMLElement);
    await pressDialog('Resolve prediction');

    expect(useSaveErrorsStore().items).toHaveLength(1);
    expect(row().textContent).toContain('Will it build?');
    expect(buttonIn(dialog(), 'Resolve prediction')).toBeDefined();
  });

  it('asks before it cancels, and sends nothing when the owner backs out', async () => {
    await openRunning('ACTIVE');
    await pressDialog('Cancel prediction');

    expect(confirmBox()?.textContent).toContain('Every viewer gets their points back.');
    expect(patches()).toHaveLength(0);

    const back = buttonIn(confirmBox() as HTMLElement, 'Cancel');

    await click(back);

    expect(patches()).toHaveLength(0);
    expect(row().textContent).toContain('Will it build?');
  });

  it('cancels after the confirm: one PATCH with CANCELED, and the row goes back to idle', async () => {
    await openRunning('ACTIVE');
    await pressDialog('Cancel prediction');
    await click(confirmBox()!.querySelector<HTMLElement>('[data-confirm-action]') as HTMLElement);

    expect(patches()).toEqual([
      {
        method: 'PATCH',
        path: expect.stringContaining('/live-ops/predictions/{predictionId}/end'),
        body: { status: 'CANCELED' },
      },
    ]);

    expect(row().textContent).toContain('No poll or prediction running');
  });

  it('keeps the prediction and reports a refused cancel', async () => {
    patchFails = true;
    await openRunning('ACTIVE');
    await pressDialog('Cancel prediction');
    await click(confirmBox()!.querySelector<HTMLElement>('[data-confirm-action]') as HTMLElement);

    expect(useSaveErrorsStore().items).toHaveLength(1);
    expect(row().textContent).toContain('Will it build?');
  });
});
