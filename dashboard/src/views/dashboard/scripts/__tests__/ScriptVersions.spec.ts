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

import CodeEditor from '@/components/layout/CodeEditor.vue';
import type {
  EditorHistoryRunner,
  VersionsPage,
  VersionSummary,
} from '@/composables/useEditorBridge';
import { i18n } from '@/i18n';

import ScriptDetail from '../components/ScriptDetail.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  id: string;
  versionId: string;
  page: unknown;
};

const SAVED = [
  {
    id: 'v3',
    version: 3,
    validationStatus: 'Valid',
  },
  {
    id: 'v2',
    version: 2,
    validationStatus: 'Valid',
  },
  {
    id: 'v1',
    version: 1,
    validationStatus: 'Invalid',
  },
] as const satisfies readonly Record<string, unknown>[];

const state = vi.hoisted(() => ({
  calls: [] as Call[],
  saved: [] as {
    id: string;
    version: number;
    validationStatus: string;
  }[],
  liveId: 'v3',
  pageSize: 2,
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options) => {
      const params = options?.params;

      state.calls.push({
        method: 'GET',
        path,
        id: String(params?.path?.id ?? ''),
        versionId: '',
        page: params?.query?.Page,
      });

      if (path.endsWith('/versions')) {
        const page = Number(params?.query?.Page ?? 1);
        const start = (page - 1) * state.pageSize;

        return {
          data: {
            data: state.saved.slice(start, start + state.pageSize),
            hasMore: start + state.pageSize < state.saved.length,
          },
          response: { status: 200, ok: true },
        };
      }

      if (path.endsWith('/project'))
        return {
          data: {
            data: {
              files: { 'index.ts': 'nnz.chat.send("hi")' },
              manifest: {
                entry: 'index.ts',
                kind: 'script',
                framework: 'none',
              },
            },
          },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/sdk/types.d.ts'))
        return { data: 'declare const nnz: unknown;', response: { status: 200, ok: true } };

      if (path.endsWith('/blast-radius'))
        return {
          data: { data: {
            categories: [],
            isMinimum: false,
            totalReferences: 0,
          } },
          response: { status: 200, ok: true },
        };

      if (path.endsWith('/test-triggers'))
        return { data: { data: [] }, response: { status: 200, ok: true } };

      return {
        data: {
          data: {
            id: 's1',
            name: 'Hype Train Banner',
            description: 'Hype Train Banner description',
            isEnabled: true,
            language: 'typescript',
            currentVersionId: state.liveId,
            currentVersion: {
              version: 3,
              declaredCapabilities: [],
              sourceCode: 'nnz.chat.send("hi")',
              validationStatus: 'Valid',
              validationErrors: [],
            },
            createdAt: '2026-01-01T10:00:00Z',
            updatedAt: '2026-01-01T10:00:00Z',
          },
        },
        response: { status: 200, ok: true },
      };
    },
    POST: async (path: string, options?: Options) => {
      state.calls.push({
        method: 'POST',
        path,
        id: String(options?.params?.path?.id ?? ''),
        versionId: String(options?.params?.path?.versionId ?? ''),
        page: undefined,
      });

      if (path.endsWith('/publish')) {
        state.liveId = String(options?.params?.path?.versionId);
      }

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
    PUT: async (path: string, options?: Options) => {
      state.calls.push({
        method: 'PUT',
        path,
        id: String(options?.params?.path?.id ?? ''),
        versionId: '',
        page: undefined,
      });

      state.saved = [
        {
          id: 'v4',
          version: 4,
          validationStatus: 'Valid',
        },
        ...state.saved,
      ];

      state.liveId = 'v4';

      return { data: undefined, response: {
        status: 200,
        ok: true,
        statusText: 'OK',
      } };
    },
    DELETE: async (path: string, options?: Options) => {
      const versionId = String(options?.params?.path?.versionId ?? '');

      state.calls.push({
        method: 'DELETE',
        path,
        id: String(options?.params?.path?.id ?? ''),
        versionId,
        page: undefined,
      });

      state.saved = state.saved.filter((entry) => entry.id !== versionId);

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => key === 'code:script:author',
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
        'sdkTypes',
        'sdkTypesUnavailable',
        'initialHistory',
        'history',
        'save',
      ],
      emits: ['close'],
      template: '<div data-code-editor />',
    }),
  };
});

let mounted: VueWrapper | null = null;

beforeEach(() => {
  state.calls = [];
  state.saved = SAVED.map((entry) => ({ ...entry }));
  state.liveId = 'v3';
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function openEditor(): Promise<VueWrapper> {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(ScriptDetail, {
    props: { scriptId: 's1' },
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  const edit = [...document.body.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === 'Edit code',
  );

  edit!.click();
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();

  return mounted;
}

const ids = (list: readonly VersionSummary[]): string[] => list.map((entry) => entry.id);
const mutations = (): Pick<Call, 'method' | 'path' | 'id' | 'versionId'>[] =>
  state.calls
    .filter((call) => call.method !== 'GET' && call.method !== 'PUT')
    .map(({ method, path, id, versionId }) => ({
      method,
      path,
      id,
      versionId,
    }));

describe('the script editor shows the saved versions', () => {
  it('opens on the first page and marks the live version', async () => {
    const view = await openEditor();
    const editor = view.findComponent(CodeEditor);
    const first = editor.props('initialHistory') as VersionsPage;

    expect(ids(first.versions)).toEqual(['v3', 'v2']);
    expect(first.hasMore).toBe(true);

    expect(first.versions.map((entry) => entry.isCurrent)).toEqual([
      true,
      false,
    ]);

    const asked = state.calls.filter((call) => call.path.endsWith('/versions'));

    expect(asked.map((call) => call.id)).toEqual(['s1']);
    expect(asked.map((call) => call.page)).toEqual([1]);
  });

  it('loads the next page and keeps the first one in the list', async () => {
    const view = await openEditor();
    const history = view.findComponent(CodeEditor).props('history') as EditorHistoryRunner;
    const outcome = await history.loadMore();

    expect(outcome.ok).toBe(true);

    if (outcome.ok) {
      expect(ids(outcome.value.versions)).toEqual(['v3', 'v2', 'v1']);
      expect(outcome.value.hasMore).toBe(false);
    }

    const pages = state.calls.filter((call) => call.path.endsWith('/versions')).map((c) => c.page);

    expect(pages).toEqual([1, 2]);
  });

  it('makes an older version the live one through the publish call', async () => {
    const view = await openEditor();
    const history = view.findComponent(CodeEditor).props('history') as EditorHistoryRunner;
    const outcome = await history.rollback('v2');

    expect(mutations()).toEqual([
      {
        method: 'POST',
        path: '/api/v{version}/code-scripts/{id}/versions/{versionId}/publish',
        id: 's1',
        versionId: 'v2',
      },
    ]);

    expect(outcome.ok).toBe(true);

    if (outcome.ok) {
      expect(outcome.value.versions.map((entry) => [entry.id, entry.isCurrent])).toEqual([
        ['v3', false],
        ['v2', true],
      ]);
    }
  });

  it('deletes a version that is not live and drops it from the list', async () => {
    const view = await openEditor();
    const history = view.findComponent(CodeEditor).props('history') as EditorHistoryRunner;
    const outcome = await history.remove('v2');

    expect(mutations()).toEqual([
      {
        method: 'DELETE',
        path: '/api/v{version}/code-scripts/{id}/versions/{versionId}',
        id: 's1',
        versionId: 'v2',
      },
    ]);

    expect(outcome.ok).toBe(true);

    if (outcome.ok) {
      expect(ids(outcome.value.versions)).toEqual(['v3']);
    }

    expect(state.saved.map((entry) => entry.id)).toEqual(['v3', 'v1']);
  });

  it('does not send a delete for the live version and says why', async () => {
    const view = await openEditor();
    const history = view.findComponent(CodeEditor).props('history') as EditorHistoryRunner;
    const outcome = await history.remove('v3');

    expect(mutations()).toEqual([]);
    expect(outcome).toEqual({ ok: false, message: en.dashboard.scripts.versions.deleteLive });
  });

  it('shows a version saved from the editor in the list afterward', async () => {
    const view = await openEditor();
    const editor = view.findComponent(CodeEditor);
    const feedback = await editor.props('save')({ 'index.ts': 'nnz.chat.send("changed")' });

    expect(feedback.ok).toBe(true);

    const history = editor.props('history') as EditorHistoryRunner;
    const outcome = await history.rollback('v4');

    expect(outcome.ok).toBe(true);

    if (outcome.ok) {
      expect(outcome.value.versions.map((entry) => [entry.id, entry.isCurrent])).toEqual([
        ['v4', true],
        ['v3', false],
      ]);
    }
  });
});
