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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref, type Ref } from 'vue';

import type { ObsState } from '@/api/obs';
import { useObsStats } from '@/api/obsOutputs';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import OutputsPanel from '../components/OutputsPanel.vue';

type Post = { path: string; body: unknown };

const posts: Post[] = [];
let statsReads: number = 0;

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/obs/stats')) {
        statsReads += 1;
      }

      return {
        data: { data: { cpuUsage: 3 } },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, init: { body?: unknown }) => {
      posts.push({ path, body: init.body });

      return { data: { data: {} }, response: { status: 200, ok: true } };
    },
  },
}));

const idle: ObsState = {
  currentScene: 'Main',
  streaming: false,
  recording: false,
  recordPaused: false,
  replayBufferActive: false,
  recordTimecode: null,
  streamTimecode: null,
};

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  statsReads = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.useRealTimers();
});

function show(
  state: ObsState,
  virtualCam: boolean = false,
  timers: { streamTimer: string; recordTimer: string } = { streamTimer: '', recordTimer: '' },
): void {
  mounted = mount(OutputsPanel, {
    attachTo: document.body,
    props: {
      channelId: 'c1',
      state,
      virtualCam,
      ...timers,
      panelState: 'ready',
      offline: false,
    },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });
}

async function press(selector: string): Promise<void> {
  const button = document.body.querySelector<HTMLButtonElement>(selector);

  expect(button).not.toBeNull();
  button?.click();
  await flushPromises();
}

describe('OutputsPanel', () => {
  it('starts the stream with the start action and no confirm', async () => {
    show(idle);
    await press('[data-output="streaming"] [data-act="start"]');

    expect(posts).toEqual([
      { path: '/api/v{version}/channels/{channelId}/obs/streaming', body: { action: 0 } },
    ]);
  });

  it('asks before stopping the stream and sends nothing until confirmed', async () => {
    show(
      {
        ...idle,
        streaming: true,
      },
      false,
      { streamTimer: '1:02:03', recordTimer: '' },
    );

    expect(document.body.querySelector('[data-output="streaming"] [data-output-timer]')?.textContent?.trim()).toBe('1:02:03');

    await press('[data-output="streaming"] [data-act="stop"]');

    expect(posts).toEqual([]);
    expect(document.body.textContent).toContain(en.dashboard.obs.outputs.stopStream.title);

    await press('[data-confirm-action]');

    expect(posts).toEqual([
      { path: '/api/v{version}/channels/{channelId}/obs/streaming', body: { action: 1 } },
    ]);
  });

  it('keeps the stream running when the confirm is canceled', async () => {
    show({ ...idle, streaming: true });
    await press('[data-output="streaming"] [data-act="stop"]');
    await press('[data-slot="alert-dialog-cancel"]');

    expect(posts).toEqual([]);
  });

  it('records, pauses, splits and stops', async () => {
    show({
      ...idle,
      recording: true,
      recordTimecode: '00:00:09.000',
    });

    await press('[data-output="recording"] [data-act="pause"]');
    await press('[data-output="recording"] [data-act="split"]');
    await press('[data-output="recording"] [data-act="stop"]');

    expect(posts.map((post) => post.body)).toEqual([
      { action: 3 },
      { action: 5 },
      { action: 1 },
    ]);
  });

  it('offers resume instead of pause while the recording is paused', async () => {
    show({
      ...idle,
      recording: true,
      recordPaused: true,
    });

    expect(document.body.querySelector('[data-output="recording"] [data-act="pause"]')).toBeNull();

    await press('[data-output="recording"] [data-act="resume"]');

    expect(posts[0]?.body).toEqual({ action: 4 });
  });

  it('starts the replay buffer, and saves a clip only while it runs', async () => {
    show(idle);

    expect(document.body.querySelector('[data-act="save"]')).toBeNull();

    await press('[data-output="replay-buffer"] [data-act="start"]');

    expect(posts[0]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/replay-buffer',
      body: { action: 0 },
    });

    mounted?.unmount();
    show({ ...idle, replayBufferActive: true });
    await press('[data-act="save"]');

    expect(posts[1]?.path).toBe('/api/v{version}/channels/{channelId}/obs/replay-buffer/save');
  });

  it('stops the virtual camera when it is running', async () => {
    show(idle, true);
    await press('[data-output="virtual-cam"] [data-act="stop"]');

    expect(posts).toEqual([
      { path: '/api/v{version}/channels/{channelId}/obs/virtual-cam', body: { action: 1 } },
    ]);
  });
});

describe('OutputsPanel rows', () => {
  it('renders one compact row per output with its name, state and actions', () => {
    show(
      {
        ...idle,
        streaming: true,
        recording: true,
      },
      false,
      { streamTimer: '0:10', recordTimer: '0:05' },
    );

    const rows = [...document.body.querySelectorAll<HTMLElement>('[data-output-row]')];

    expect(rows.map((row) => row.dataset.output)).toEqual([
      'streaming',
      'recording',
      'replay-buffer',
      'virtual-cam',
    ]);

    const stream = rows[0];

    expect(stream?.querySelector('[data-output-state]')?.textContent?.trim()).toBe(en.dashboard.obs.outputs.live);
    expect(stream?.querySelector('[data-output-timer]')?.textContent?.trim()).toBe('0:10');
    expect(rows[1]?.querySelector('[data-output-timer]')?.textContent?.trim()).toBe('0:05');
    expect(rows[2]?.querySelector('[data-output-timer]')).toBeNull();
    expect(document.body.querySelectorAll('[data-output-row] [data-act="stop"]')).toHaveLength(2);
  });

  it('shows the virtual camera as running when the read says so', () => {
    show(idle, true);

    const state = document.body.querySelector('[data-output="virtual-cam"] [data-output-state]');

    expect(state?.textContent?.trim()).toBe(en.dashboard.obs.outputs.active);
  });
});

describe('useObsStats', () => {
  function harness(active: Ref<boolean>): VueWrapper {
    const Probe = defineComponent({
      setup() {
        useObsStats(ref('c1'), ref(true), active, 5000);

        return () => h('div');
      },
    });

    return mount(Probe, {
      global: { plugins: [[VueQueryPlugin, { queryClient: new QueryClient() }]] },
    });
  }

  it('polls while the page is visible and stops while it is hidden', async () => {
    vi.useFakeTimers();

    const active = ref(true);
    const probe = harness(active);

    await vi.advanceTimersByTimeAsync(11000);

    const whileVisible = statsReads;

    expect(whileVisible).toBeGreaterThanOrEqual(3);

    active.value = false;
    await vi.advanceTimersByTimeAsync(100);

    const atHide = statsReads;

    await vi.advanceTimersByTimeAsync(30000);

    expect(statsReads).toBe(atHide);

    active.value = true;
    await vi.advanceTimersByTimeAsync(5100);

    expect(statsReads).toBeGreaterThan(atHide);
    probe.unmount();
  });
});
