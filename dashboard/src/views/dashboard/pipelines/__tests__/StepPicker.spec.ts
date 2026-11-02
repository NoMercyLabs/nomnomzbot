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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { computed, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import type { PickerKind, PipelineOptionList, PipelineStep } from '@/api/pipelines';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import StepField from '../components/StepField.vue';
import StepPicker from '../components/StepPicker.vue';
import { actionRows, choiceContext } from '../components/stepFields';

// A list is keyed by its kind, or by `kind:provider` when the step's context names a provider.
const lists = vi.hoisted(() => ({
  byKind: {} as Record<string, unknown>,
  // The context each list was asked with, newest last.
  asked: [] as Record<string, string>[],
}));

vi.mock('@/api/pipelines', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/pipelines')>()),
  usePipelineOptions: (
    _channel: unknown,
    kind: () => string,
    _search: unknown,
    context?: { value: Record<string, string> },
  ) => ({
    data: computed(() => {
      lists.asked.push({ ...context?.value });

      return lists.byKind[`${kind()}:${context?.value.provider}`] ?? lists.byKind[kind()];
    }),
    isPending: ref(false),
    isFetching: ref(false),
  }),
  usePipelineOption: () => ({ data: computed(() =>
    null) }),
}));

const held = vi.hoisted(() => ({ keys: ['code:script:author'] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const scripts: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: '0192f0aa-0000-7000-8000-00000000a001',
      label: 'welcome-viewer',
      secondaryText: 'Greets a new viewer',
      state: 0,
    },
    {
      value: '0192f0aa-0000-7000-8000-00000000a002',
      label: 'switched-off',
      reason: 'Script is disabled.',
      state: 1,
    },
  ],
  totalCount: 2,
};

const playlists: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: '6GaEADx2wNUWA6DJsMVcct',
      label: 'Banger',
      secondaryText: '42 tracks',
      state: 0,
    },
  ],
  totalCount: 1,
};

let router: Router;

async function openRouter(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/pipelines/:id',
        name: 'dashboard-pipeline',
        component: blank,
      },
      {
        path: '/app/scripts',
        name: 'dashboard-scripts',
        component: blank,
      },
    ],
  });

  await router.push('/app/pipelines/p1?step=s9');
}

async function openPicker(
  kind: PickerKind,
  value = '',
  context?: Record<string, string>,
): Promise<VueWrapper> {
  await openRouter();

  mounted = mount(StepPicker, {
    attachTo: document.body,
    props: {
      id: 'picker',
      kind,
      value,
      required: true,
      context,
    },
    global: { plugins: [i18n, router] },
  });

  document.body.querySelector<HTMLButtonElement>('#picker')!.click();
  await flushPromises();

  return mounted;
}

describe('editing the chosen script', () => {
  const editButton = (): HTMLElement | null =>
    document.body.querySelector<HTMLElement>('[data-picker-edit]');

  it('opens the script in the editor with this step as the way back', async () => {
    lists.byKind.script = scripts;
    held.keys = ['code:script:author'];
    i18n.global.mergeLocaleMessage('en', en);
    await openPicker('script', '0192f0aa-0000-7000-8000-00000000a001');

    expect(editButton()?.textContent?.trim()).toBe('Edit script');
    expect(editButton()?.getAttribute('data-variant')).toBe('outline');

    editButton()!.click();
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('dashboard-scripts');

    expect(router.currentRoute.value.query).toEqual({
      script: '0192f0aa-0000-7000-8000-00000000a001',
      back: '/app/pipelines/p1?step=s9',
    });
  });

  it('is hidden without the scripts right, with no script chosen, and on other pickers', async () => {
    lists.byKind.script = scripts;
    held.keys = [];
    await openPicker('script', '0192f0aa-0000-7000-8000-00000000a001');

    expect(editButton()).toBeNull();

    mounted?.unmount();
    held.keys = ['code:script:author'];
    await openPicker('script', '');

    expect(editButton()).toBeNull();

    mounted?.unmount();
    lists.byKind.music_playlist = playlists;
    await openPicker('music_playlist', '6GaEADx2wNUWA6DJsMVcct');

    expect(editButton()).toBeNull();
  });
});

describe('the script and playlist pickers', () => {
  it('lists the scripts by name and saves the id of the one picked', async () => {
    lists.byKind.script = scripts;
    const picker = await openPicker('script');

    document.body.querySelector<HTMLElement>('[data-picker-option="0192f0aa-0000-7000-8000-00000000a001"]')!.click();
    await flushPromises();

    expect(picker.emitted('change')).toEqual([['0192f0aa-0000-7000-8000-00000000a001']]);
  });

  it('shows a disabled script with its reason, off for choosing', async () => {
    lists.byKind.script = scripts;
    await openPicker('script');
    const off = document.body.querySelector<HTMLElement>('[data-picker-option="0192f0aa-0000-7000-8000-00000000a002"]')!;

    expect(off.hasAttribute('data-disabled')).toBe(true);
    expect(off.textContent).toContain('Script is disabled.');
  });

  it('lists the playlists of the service the step chose, not the active one', async () => {
    lists.byKind.music_playlist = playlists;

    lists.byKind['music_playlist:youtube'] = {
      sourceAvailable: true,
      items: [{
        value: 'PLyt1',
        label: 'Stream tunes',
        secondaryText: '7 tracks · youtube',
        state: 0,
      }],
      totalCount: 1,
    };

    await openPicker('music_playlist', '', { provider: 'youtube' });

    expect(document.body.querySelector('[data-picker-option="PLyt1"]')?.textContent).toContain('Stream tunes');
    expect(document.body.querySelector('[data-picker-option="6GaEADx2wNUWA6DJsMVcct"]')).toBeNull();
  });

  it('lists the playlists by name and saves the provider playlist id', async () => {
    lists.byKind.music_playlist = playlists;
    const picker = await openPicker('music_playlist');

    expect(document.body.querySelector('[data-picker-option="6GaEADx2wNUWA6DJsMVcct"]')?.textContent).toContain('Banger');

    document.body.querySelector<HTMLElement>('[data-picker-option="6GaEADx2wNUWA6DJsMVcct"]')!.click();
    await flushPromises();

    expect(picker.emitted('change')).toEqual([['6GaEADx2wNUWA6DJsMVcct']]);
  });

  it('says why the playlists cannot be listed when no music is connected', async () => {
    lists.byKind.music_playlist = {
      sourceAvailable: false,
      unavailableReason: 'No music provider is connected.',
      items: [],
      totalCount: 0,
    } as const satisfies PipelineOptionList;

    await openPicker('music_playlist');

    expect(document.body.querySelector('[data-picker-unavailable]')?.textContent?.trim()).toBe('No music provider is connected.');
    expect(document.body.querySelector('[data-picker-option]')).toBeNull();
  });
});

describe('the step form for script and playlist settings', () => {
  const words = {
    t: (key: string) =>
      key,
    te: () =>
      false,
    text: (_text: unknown, fallback: string) =>
      fallback,
  };
  const field = (name: string, kind: string) => ({
    name,
    kind,
    required: true,
    repeatable: false,
    description: null,
    templated: false,
    declaresVariable: false,
  }) as const satisfies Record<string, unknown>;

  it('gives a script id and a playlist id the picker control instead of a text box', () => {
    const rows = actionRows(
      {
        id: 's1',
        parent_step_id: null,
        branch: null,
        block_kind: null,
        block_config: null,
        order: 0,
        action: {
          type: 'x',
          code_script_id: 'abc',
          playlist_id: '6Ga',
        },
        condition: null,
        continue_on_error: false,
        stop_on_match: false,
      },
      {
        type: 'x',
        category: { key: 'c' },
        description: { key: 'd' },
        fields: [field('code_script_id', 'script'), field('playlist_id', 'music_playlist')],
      } as never,
      words,
    );

    expect(rows.map((row) =>
      `${row.name}:${row.control}:${String(row.value)}`)).toEqual([
      'code_script_id:pick:abc',
      'playlist_id:pick:6Ga',
    ]);
  });
});

describe('the OBS name settings of a step', () => {
  const scenes: PipelineOptionList = {
    sourceAvailable: true,
    items: [
      {
        value: 'Gameplay',
        label: 'Gameplay',
        secondaryText: 'live',
        state: 0,
      },
      {
        value: 'Starting',
        label: 'Starting',
        state: 0,
      },
    ],
    totalCount: 2,
  };

  const sources: PipelineOptionList = {
    sourceAvailable: true,
    items: [{
      value: 'Webcam',
      label: 'Webcam',
      secondaryText: 'visible',
      state: 0,
    }],
    totalCount: 1,
  };

  const words = {
    t: (key: string) =>
      key,
    te: () =>
      false,
    text: (_text: unknown, fallback: string) =>
      fallback,
  };

  const field = (name: string, kind: string) => ({
    name,
    kind,
    required: true,
    repeatable: false,
    description: null,
    templated: false,
    declaresVariable: false,
  }) as const satisfies Record<string, unknown>;

  const setSource = {
    type: 'obs_set_source',
    category: { key: 'c' },
    description: { key: 'd' },
    fields: [field('scene', 'obs_scene'), field('source', 'obs_scene_item')],
  } as never;

  function stepWith(action: Record<string, string>): PipelineStep {
    return {
      id: 's1',
      parent_step_id: null,
      branch: null,
      block_kind: null,
      block_config: null,
      order: 0,
      action: { type: 'obs_set_source', ...action },
      condition: null,
      continue_on_error: false,
      stop_on_match: false,
    };
  }

  // The step form as the inspector builds it: rows from the catalog, context from the rows.
  async function openStepField(name: string, action: Record<string, string>): Promise<VueWrapper> {
    const rows = actionRows(stepWith(action), setSource, words);
    const row = rows.find((candidate) =>
      candidate.name === name)!;

    await openRouter();
    lists.asked.length = 0;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row, context: choiceContext(rows) },
      global: { plugins: [i18n, router] },
    });

    document.body.querySelector<HTMLButtonElement>('button[role="combobox"]')!.click();
    await flushPromises();

    return mounted;
  }

  it('shows the scene and source settings as dropdowns of what OBS lists, not text boxes', async () => {
    i18n.global.mergeLocaleMessage('en', en);
    lists.byKind.obs_scene = scenes;
    lists.byKind.obs_scene_item = sources;

    const form = await openStepField('scene', {});

    expect(form.element.querySelector('input, textarea')).toBeNull();
    expect(document.body.querySelector('[data-picker-option="Gameplay"]')?.textContent).toContain('live');
    expect(document.body.querySelector('[data-picker-option="Starting"]')).not.toBeNull();
  });

  it('asks for the sources of the scene the step saved, and saves the plain source name', async () => {
    lists.byKind.obs_scene_item = sources;
    const form = await openStepField('source', { scene: 'Gameplay' });

    expect(lists.asked.at(-1)).toEqual({ scene: 'Gameplay' });

    document.body.querySelector<HTMLElement>('[data-picker-option="Webcam"]')!.click();
    await flushPromises();

    expect(form.emitted('change')).toEqual([['Webcam']]);
  });

  it('asks for every source while no scene is saved', async () => {
    lists.byKind.obs_scene_item = sources;
    await openStepField('source', {});

    expect(lists.asked.at(-1)).toEqual({});
  });

  it('keeps a saved scene that OBS no longer has, shows its name and flags it as missing', async () => {
    lists.byKind.obs_scene = scenes;
    await openStepField('scene', { scene: 'Old BRB scene' });

    const button = document.body.querySelector<HTMLElement>('button[role="combobox"]')!;

    expect(button.textContent).toContain('Old BRB scene');
    expect(button.querySelector('[data-picker-missing]')?.textContent?.trim()).toBe('Not in the list');
  });

  it('does not flag a saved scene that OBS lists, a template, or a list that could not be read', async () => {
    lists.byKind.obs_scene = scenes;
    await openStepField('scene', { scene: 'Gameplay' });

    expect(document.body.querySelector('[data-picker-missing]')).toBeNull();

    mounted?.unmount();
    await openStepField('scene', { scene: '{{user.name}}' });

    expect(document.body.querySelector('[data-picker-missing]')).toBeNull();

    mounted?.unmount();

    lists.byKind.obs_scene = {
      sourceAvailable: false,
      unavailableReason: 'OBS is not connected.',
      items: [],
      totalCount: 0,
    } as const satisfies PipelineOptionList;

    await openStepField('scene', { scene: 'Old BRB scene' });

    expect(document.body.querySelector('[data-picker-missing]')).toBeNull();
    expect(document.body.querySelector('[data-picker-unavailable]')?.textContent?.trim()).toBe('OBS is not connected.');
  });
});
