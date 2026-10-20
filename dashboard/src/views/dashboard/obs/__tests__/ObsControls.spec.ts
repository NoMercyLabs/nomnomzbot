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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { Select } from '@/components/ui/select';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ObsView from '../ObsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

type Post = { path: string; body: unknown };

const posts: Post[] = [];
let program = 'Main';
let studio = false;
let transition = 'Fade';
let postFails = false;
let noTransitions = false;

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => {
      if (path.endsWith('/overlays/required'))
        return { data: { data: [] }, response: { status: 200, ok: true } };

      if (path.endsWith('/widgets'))
        return {
          data: {
            data: [],
            hasMore: false,
            nextPage: null,
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/obs/connection'))
        return ok({
          isEnabled: true,
          mode: 'websocket',
          host: 'localhost',
          port: 4455,
          lastError: null,
        });

      if (path.endsWith('/obs/bridge/status'))
        return ok({ instanceCount: 1, hasLeader: true });

      if (path.endsWith('/obs/state'))
        return ok({
          currentScene: program,
          streaming: false,
          recording: false,
        });

      if (path.endsWith('/obs/studio-mode'))
        return ok({ enabled: studio });

      if (path.endsWith('/obs/scene-transitions'))
        return ok(noTransitions
          ? []
          : [
              { name: 'Cut', isCurrent: transition === 'Cut' },
              { name: 'Fade', isCurrent: transition === 'Fade' },
            ]);

      // The hotkeys panel on the tools tab reads plain hotkey names, never scene rows.
      if (path.endsWith('/obs/hotkeys'))
        return ok(['OBSBasic.StartStreaming']);

      return ok([
        { name: 'Main', isCurrent: program === 'Main' },
        { name: 'BRB', isCurrent: program === 'BRB' },
      ]);
    },
    POST: async (path: string, init: { body?: unknown }): Promise<Answer> => {
      posts.push({ path, body: init.body });

      if (postFails)
        return { error: { detail: 'OBS refused' }, response: { status: 502, ok: false } };

      const body = init.body as Record<string, unknown>;

      if (path.endsWith('/obs/scene')) {
        program = body.scene as string;
      }

      if (path.endsWith('/obs/studio-mode')) {
        studio = body.enabled as boolean;
      }

      if (path.endsWith('/obs/scene-transitions/current')) {
        transition = body.transitionName as string;
      }

      return ok({});
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  posts.length = 0;
  program = 'Main';
  studio = false;
  transition = 'Fade';
  postFails = false;
  noTransitions = false;
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

// The scenes sit on the Live tab, studio mode on the Studio and tools tab (?view=tools).
async function open(view?: 'tools'): Promise<void> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/app/obs', component: { template: '<div />' } }],
  });

  await router.push(view
    ? `/app/obs?view=${view}`
    : '/app/obs');

  mounted = mount(ObsView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        router,
        [VueQueryPlugin, { queryClient }],
      ],
    },
  });

  await settle();
}

// The studio panel's two pickers; the trigger of each carries its name (data-obs-picker).
const picker = (name: 'transition' | 'preview') => {
  const found = mounted
    ?.findAllComponents(Select)
    .find((select) => select.find(`[data-obs-picker="${name}"]`).exists());

  if (!found)
    throw new Error(`no ${name} picker`);

  return found;
};

const sceneRow = (name: string): HTMLElement => {
  const rows = Array.from(document.querySelectorAll<HTMLElement>('[data-obs-scene]'));
  const row = rows.find((candidate) => candidate.textContent?.includes(name));

  if (!row)
    throw new Error(`no scene row ${name}`);

  return row;
};

const current = (): string[] =>
  Array.from(document.querySelectorAll('[data-obs-scene][data-current="true"]')).map(
    (row) => row.querySelector('span.font-medium')?.textContent?.trim() ?? '',
  );

describe('OBS scene controls', () => {
  it('sends the switch request and marks the clicked scene as the program scene', async () => {
    await open();
    expect(current()).toEqual(['Main']);

    sceneRow('BRB').click();
    await settle();

    expect(posts).toEqual([
      { path: '/api/v{version}/channels/{channelId}/obs/scene', body: { scene: 'BRB' } },
    ]);

    expect(current()).toEqual(['BRB']);
  });

  it('rolls the mark back to the old scene when OBS refuses the switch', async () => {
    postFails = true;
    await open();

    sceneRow('BRB').click();
    await settle();

    expect(posts).toHaveLength(1);
    expect(current()).toEqual(['Main']);
  });

  it('draws no empty studio body when OBS has no transitions and studio mode is off', async () => {
    noTransitions = true;
    await open('tools');

    expect(document.querySelector('[data-obs-studio-toggle]')).not.toBeNull();
    expect(document.querySelector('[data-obs-studio-body]')).toBeNull();
  });

  it('hides the preview and transition controls until studio mode is on, then fires the transition with its duration', async () => {
    await open('tools');
    expect(document.querySelector('[data-obs-transition-button]')).toBeNull();

    document.querySelector<HTMLElement>('[data-obs-studio-toggle] [role="switch"]')?.click();
    await settle();

    expect(posts[0]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/studio-mode',
      body: { enabled: true },
    });

    picker('preview').vm.$emit('update:modelValue', 'BRB');
    await settle();

    expect(posts[1]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/scene/preview',
      body: { scene: 'BRB' },
    });

    const duration = document.querySelector<HTMLInputElement>('[data-obs-duration]');

    // The streamer types seconds; OBS takes milliseconds.
    if (duration) {
      duration.value = '0.3';
      duration.dispatchEvent(new Event('input'));
    }

    await settle();
    document.querySelector<HTMLElement>('[data-obs-transition-button]')?.click();
    await settle();

    expect(posts[2]).toEqual({
      path: '/api/v{version}/channels/{channelId}/obs/studio-mode/transition',
      body: { durationMs: 300 },
    });
  });

  it('picks another transition and marks it as the active one', async () => {
    await open('tools');

    expect(picker('transition').props('modelValue')).toBe('Fade');

    picker('transition').vm.$emit('update:modelValue', 'Cut');
    await settle();

    expect(posts).toEqual([
      {
        path: '/api/v{version}/channels/{channelId}/obs/scene-transitions/current',
        body: { transitionName: 'Cut' },
      },
    ]);

    expect(picker('transition').props('modelValue')).toBe('Cut');
  });
});
