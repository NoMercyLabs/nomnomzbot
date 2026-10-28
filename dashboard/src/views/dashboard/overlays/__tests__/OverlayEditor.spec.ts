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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { Widget } from '@/api/widgets';
import CodeEditor from '@/components/layout/CodeEditor.vue';
import type { CompileFeedback, EditorHistoryRunner } from '@/composables/useEditorBridge';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import OverlayDetail from '../components/OverlayDetail.vue';

const widget = {
  id: 'w1',
  name: 'Feather Alert',
  description: 'Feather Alert description',
  framework: 'vue',
  source: 'first_party',
  isEnabled: true,
  overlayUrl: 'https://bot.example/overlay?widgetId=w1',
  activeVersionId: 'v2',
  galleryItemId: null,
  settings: { width: 400 },
  eventSubscriptions: ['follow'],
  lastRuntimeError: null,
  lastRanAt: null,
  createdAt: '2026-01-01T10:00:00Z',
  updatedAt: '2026-01-01T10:00:00Z',
  galleryUpdateAvailable: false,
  isAttached: false,
  isCustomized: false,
} as unknown as Widget;

const manifest = { entry: 'src/App.vue', dependencies: {} };
const files = { 'src/App.vue': '<template><p>hi</p></template>' };

const held = vi.hoisted(() => ({ keys: [] as string[] }));
const calls = vi.hoisted(() => ({
  save: [] as unknown[][],
  rollback: [] as unknown[][],
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => held.keys.includes(key),
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/components/layout/CodeEditor.vue', async () => {
  const { defineComponent: define } = await import('vue');

  return {
    default: define({
      name: 'CodeEditor',
      props: [
        'title',
        'files',
        'entry',
        'language',
        'sdkTypes',
        'sdkTypesUnavailable',
        'widget',
        'fireSamples',
        'fireSamplesError',
        'eventSubscriptions',
        'initialHistory',
        'history',
        'save',
        'previewAction',
      ],
      emits: ['close'],
      template: '<div data-code-editor />',
    }),
  };
});

vi.mock('@/api/widgetCode', () => ({
  fetchWidgetProject: async () => ({ files, manifest }),
  fetchWidgetSdkTypes: async () => 'declare const nnz: unknown;',
  fetchWidgetSamples: async () => ({ follow: { userName: 'sample' } }),
  fetchWidgetVersions: async () => ({
    versions: [
      {
        id: 'v2',
        version: 2,
        validationStatus: 'success',
        isCurrent: true,
      },
      {
        id: 'v1',
        version: 1,
        validationStatus: 'success',
        isCurrent: false,
      },
    ],
    hasMore: false,
  }),
  saveWidgetProject: async (...args: unknown[]): Promise<CompileFeedback> => {
    calls.save.push(args);

    return { ok: true, message: '' };
  },
  rollbackWidget: async (...args: unknown[]): Promise<void> => {
    calls.rollback.push(args);
  },
  runWidgetPreviewAction: async () => ({
    success: true,
    output: null,
    error: null,
    errorCode: null,
    variables: {},
  }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  held.keys = [];
  calls.save = [];
  calls.rollback = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function show(keys: string[]): VueWrapper {
  held.keys = keys;

  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(OverlayDetail, {
    props: { widget },
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  return mounted;
}

async function openEditor(view: VueWrapper): Promise<void> {
  await view.get('[data-edit-code]').trigger('click');
  await flushPromises();
}

describe('the overlay page opens the code editor', () => {
  it('has no edit button without widget:write', () => {
    const view = show(['widget:read']);

    expect(view.find('[data-edit-code]').exists()).toBe(false);
  });

  it('opens the editor on the widget files, samples, events and versions', async () => {
    const view = show(['widget:read', 'widget:write']);

    expect(view.findComponent(CodeEditor).exists()).toBe(false);

    await openEditor(view);

    const editor = view.findComponent(CodeEditor);

    expect(editor.props('files')).toEqual(files);
    expect(editor.props('entry')).toBe('src/App.vue');
    expect(editor.props('title')).toBe('Feather Alert');
    expect(editor.props('language')).toBe('vue');
    expect(editor.props('sdkTypes')).toBe('declare const nnz: unknown;');

    expect(editor.props('widget')).toEqual({
      id: 'w1',
      name: 'Feather Alert',
      settings: { width: 400 },
    });

    expect(editor.props('eventSubscriptions')).toEqual(['follow']);
    expect(editor.props('fireSamples')).toEqual({ follow: { userName: 'sample' } });

    const shownVersions = editor.props('initialHistory')?.versions ?? [];

    expect(shownVersions.map((entry: { id: string }) => entry.id)).toEqual([
      'v2',
      'v1',
    ]);
  });

  it('saves the edited files with the manifest and closes back to the page', async () => {
    const view = show(['widget:read', 'widget:write']);

    await openEditor(view);

    const editor = view.findComponent(CodeEditor);
    const edited = { 'src/App.vue': '<template><p>changed</p></template>' };
    const feedback: CompileFeedback = await editor.props('save')(edited);

    expect(calls.save).toEqual([['c1', 'w1', { files: edited, manifest }]]);
    expect(feedback.ok).toBe(true);
    expect(feedback.message).toBe(en.dashboard.overlays.editor.compiled);

    editor.vm.$emit('close');
    await flushPromises();

    expect(view.findComponent(CodeEditor).exists()).toBe(false);
  });

  it('rolls back through the bot and marks that version as the current one', async () => {
    const view = show(['widget:read', 'widget:write']);

    await openEditor(view);

    const history = view.findComponent(CodeEditor).props('history') as EditorHistoryRunner;
    const outcome = await history.rollback('v1');

    expect(calls.rollback).toEqual([['c1', 'w1', 'v1']]);

    expect(outcome).toEqual({
      ok: true,
      value: {
        hasMore: false,
        versions: [
          {
            id: 'v2',
            version: 2,
            validationStatus: 'success',
            isCurrent: false,
          },
          {
            id: 'v1',
            version: 1,
            validationStatus: 'success',
            isCurrent: true,
          },
        ],
      },
    });
  });
});
