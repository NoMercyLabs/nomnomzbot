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
import { createPinia, setActivePinia } from 'pinia';
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useSaveErrorsStore } from '@/stores/saveErrors';

import {
  codeScriptKey,
  codeScriptsKey,
  fetchBlastRadius,
  fetchCodeScripts,
  fetchScriptVersions,
  fetchTestTriggers,
  deleteScriptVersion,
  publishScriptVersion,
  runScriptTest,
  toEditorOutcome,
  useCreateCodeScript,
  useDeleteCodeScript,
  useSetScriptEnabled,
  type CodeScriptSummary,
  type TestRunOutcome,
} from '../codeScripts';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: {
    ok: boolean;
    status: number;
    headers: Headers;
  };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PATCH: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('../client', () => ({ client: http }));

const ok = {
  ok: true,
  status: 200,
  headers: new Headers(),
} as const satisfies Answer['response'];
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

function row(id: string, isEnabled: boolean): CodeScriptSummary {
  return {
    id,
    name: `Script ${id}`,
    description: null,
    isEnabled,
    currentVersion: 1,
    currentValidationStatus: 'valid',
    lastRuntimeError: null,
    lastRanAt: null,
    createdAt: '2026-10-01T10:00:00Z',
    updatedAt: '2026-10-01T10:00:00Z',
  } as CodeScriptSummary;
}

function mount<T>(use: () => T): {
  queries: QueryClient;
  hook: T;
  inbox: ReturnType<typeof useSaveErrorsStore>;
} {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
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

beforeEach(() => {
  http.GET.mockReset();
  http.POST.mockReset();
  http.PATCH.mockReset();
  http.DELETE.mockReset();
});

describe('fetchCodeScripts', () => {
  it('returns the scripts the bot lists', async () => {
    http.GET.mockResolvedValue({ data: { data: [{ id: 's1', name: 'Greeter' }] }, response: ok });

    expect(await fetchCodeScripts()).toEqual([{ id: 's1', name: 'Greeter' }]);
  });

  it('fails with the bot reason when the bot refuses, instead of reading as an empty list', async () => {
    http.GET.mockResolvedValue({
      error: {
        status: 'error',
        message: 'Custom code is not enabled for this channel. Enable it on the Features page.',
        code: 'FEATURE_DISABLED',
        args: [],
      },
      response: {
        ok: false,
        status: 403,
        headers: new Headers(),
      },
    });

    await expect(fetchCodeScripts()).rejects.toThrow(
      'Custom code is not enabled for this channel. Enable it on the Features page.',
    );
  });
});

describe('useSetScriptEnabled', () => {
  it('flips the row at once and sends PATCH with the id and the new state', async () => {
    let finish: (answer: Answer) => void = () => {};

    http.PATCH.mockReturnValue(new Promise<Answer>((done) => (finish = done)));
    http.GET.mockResolvedValue({ data: { data: [row('a', false), row('b', true)] }, response: ok });
    const { queries, hook } = mount(() => useSetScriptEnabled());

    queries.setQueryData(codeScriptsKey, [row('a', false), row('b', false)]);

    const flip: Promise<unknown> = hook.mutateAsync({ id: 'b', isEnabled: true });

    await settle();

    const during = queries.getQueryData<CodeScriptSummary[]>(codeScriptsKey);

    expect(during?.map((script: CodeScriptSummary) => [script.id, script.isEnabled])).toEqual([
      ['a', false],
      ['b', true],
    ]);

    const [path, init] = http.PATCH.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> }; body: unknown },
    ];

    expect(path).toBe('/api/v{version}/code-scripts/{id}/enabled');
    expect(init.params.path).toEqual({ version: '1', id: 'b' });
    expect(init.body).toEqual({ isEnabled: true });

    finish({ response: ok });
    await flip;
  });

  it('puts the row back and reports to the error inbox when the bot refuses', async () => {
    http.PATCH.mockResolvedValue({
      error: { message: 'no' },
      response: {
        ok: false,
        status: 403,
        headers: new Headers(),
      },
    });

    const { queries, hook, inbox } = mount(() => useSetScriptEnabled());

    queries.setQueryData(codeScriptsKey, [row('a', false)]);

    await expect(hook.mutateAsync({ id: 'a', isEnabled: true })).rejects.toThrow('no');

    expect(queries.getQueryData<CodeScriptSummary[]>(codeScriptsKey)?.[0]?.isEnabled).toBe(false);
    expect(inbox.items.length).toBe(1);
  });

  it('refreshes the detail entry of that script after the bot accepts', async () => {
    http.PATCH.mockResolvedValue({ response: ok });
    http.GET.mockResolvedValue({ data: { data: [row('a', true)] }, response: ok });
    const { queries, hook } = mount(() => useSetScriptEnabled());

    queries.setQueryData(codeScriptsKey, [row('a', false)]);
    queries.setQueryData(codeScriptKey('a'), { id: 'a', isEnabled: false });

    await hook.mutateAsync({ id: 'a', isEnabled: true });

    expect(queries.getQueryState(codeScriptKey('a'))?.isInvalidated).toBe(true);
  });
});

describe('useCreateCodeScript', () => {
  it('sends name, description and source, returns the new id and invalidates the list', async () => {
    http.POST.mockResolvedValue({ data: { data: { id: 'new1' } }, response: ok });
    const { queries, hook } = mount(() => useCreateCodeScript());

    queries.setQueryData(codeScriptsKey, [row('a', true)]);

    const id: string | null = await hook.mutateAsync({
      name: 'Greeter',
      description: null,
      sourceCode: 'export default {}',
    });

    expect(id).toBe('new1');
    const [path, init] = http.POST.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> }; body: unknown },
    ];

    expect(path).toBe('/api/v{version}/code-scripts');

    expect(init.body).toEqual({
      name: 'Greeter',
      description: null,
      sourceCode: 'export default {}',
    });

    expect(queries.getQueryState(codeScriptsKey)?.isInvalidated).toBe(true);
  });

  it('fails with the bot reason and leaves the list alone', async () => {
    http.POST.mockResolvedValue({
      error: { message: 'Name is taken' },
      response: {
        ok: false,
        status: 400,
        headers: new Headers(),
      },
    });

    const { queries, hook } = mount(() => useCreateCodeScript());

    queries.setQueryData(codeScriptsKey, [row('a', true)]);

    await expect(
      hook.mutateAsync({
        name: 'A',
        description: null,
        sourceCode: '',
      }),
    ).rejects.toThrow('Name is taken');

    expect(queries.getQueryState(codeScriptsKey)?.isInvalidated).toBe(false);
  });
});

describe('blast radius and delete', () => {
  const radius = {
    categories: [
      {
        categoryKey: 'pipelines',
        count: 2,
        sample: ['Raid'],
      },
    ],
    isMinimum: true,
    totalReferences: 2,
  };

  it('reads the blast radius of one script', async () => {
    http.GET.mockResolvedValue({ data: { data: radius }, response: ok });

    expect(await fetchBlastRadius('a')).toEqual(radius);
    const [path, init] = http.GET.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ];

    expect(path).toBe('/api/v{version}/code-scripts/{id}/blast-radius');
    expect(init.params.path).toEqual({ version: '1', id: 'a' });
  });

  it('asks for the blast radius before it deletes, then drops the list entry', async () => {
    const order: string[] = [];

    http.GET.mockImplementation(async () => {
      order.push('blast');

      return { data: { data: radius }, response: ok };
    });

    http.DELETE.mockImplementation(async () => {
      order.push('delete');

      return { response: ok };
    });

    const { queries, hook } = mount(() => useDeleteCodeScript());

    queries.setQueryData(codeScriptsKey, [row('a', true)]);

    await hook.mutateAsync('a');

    expect(order).toEqual(['blast', 'delete']);
    const [path, init] = http.DELETE.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ];

    expect(path).toBe('/api/v{version}/code-scripts/{id}');
    expect(init.params.path).toEqual({ version: '1', id: 'a' });
    expect(queries.getQueryState(codeScriptsKey)?.isInvalidated).toBe(true);
  });

  it('refuses to delete when the blast radius cannot be read', async () => {
    http.GET.mockResolvedValue({
      error: { message: 'Not found' },
      response: {
        ok: false,
        status: 404,
        headers: new Headers(),
      },
    });

    const { queries, hook } = mount(() => useDeleteCodeScript());

    queries.setQueryData(codeScriptsKey, [row('a', true)]);

    await expect(hook.mutateAsync('a')).rejects.toThrow('Not found');

    expect(http.DELETE).not.toHaveBeenCalled();
    expect(queries.getQueryState(codeScriptsKey)?.isInvalidated).toBe(false);
  });
});

describe('runScriptTest', () => {
  const dto = {
    success: false,
    error: 'boom',
    durationMs: '12',
    hostCallCount: 3,
    capturedEffects: [{ name: 'chat.say', argsPreview: '"hi"' }],
    chatOutput: ['hi'],
    log: [],
    variablesSet: { a: '1' },
    console: ['line'],
    timeline: [
      {
        seq: 1,
        kind: 'effect',
        text: 'chat.say',
      },
    ],
    errorPosition: {
      file: 'index.ts',
      line: 7,
      column: '4',
    },
  };

  it('sends the project only when the editor has unsaved files', async () => {
    http.POST.mockResolvedValue({ data: { data: dto }, response: ok });
    const project = {
      files: { 'index.ts': 'x' },
      manifest: {
        entry: 'index.ts',
        kind: 'script',
        framework: 'none',
        dependencies: null,
      },
    };

    await runScriptTest('a', {
      variables: {},
      args: [],
      trigger: 't1',
    });

    await runScriptTest('a', {
      variables: {},
      args: [],
      project,
    });

    const [path, saved] = http.POST.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> }; body: Record<string, unknown> },
    ];
    const [, unsaved] = http.POST.mock.calls[1] as [string, { body: Record<string, unknown> }];

    expect(path).toBe('/api/v{version}/code-scripts/{id}/test-run');
    expect(saved.params.path).toEqual({ version: '1', id: 'a' });

    expect(saved.body).toEqual({
      variables: {},
      args: [],
      trigger: 't1',
    });

    expect('project' in saved.body).toBe(false);
    expect(unsaved.body.project).toEqual(project);
  });

  it('maps the result: numbers, effects and the error position as editor errors', async () => {
    http.POST.mockResolvedValue({ data: { data: dto }, response: ok });

    const outcome: TestRunOutcome = await runScriptTest('a', { variables: {}, args: [] });

    expect(outcome).toEqual({
      kind: 'ok',
      result: {
        success: false,
        durationMs: 12,
        hostCallCount: 3,
        error: 'boom',
        chatOutput: ['hi'],
        variablesSet: { a: '1' },
        timeline: [
          {
            seq: 1,
            kind: 'effect',
            text: 'chat.say',
          },
        ],
        console: ['line'],
        effects: [{ name: 'chat.say', argsPreview: '"hi"' }],
        errors: [
          {
            message: 'boom',
            file: 'index.ts',
            line: 7,
            column: 4,
          },
        ],
      },
    });
  });

  it('reports a rate limit as its own outcome with the bot message and retry window', async () => {
    http.POST.mockResolvedValue({
      error: { message: 'Slow down' },
      response: {
        ok: false,
        status: 429,
        headers: new Headers({ 'Retry-After': '12' }),
      },
    });

    expect(await runScriptTest('a', { variables: {}, args: [] })).toEqual({
      kind: 'rateLimited',
      message: 'Slow down',
      retryAfterSeconds: 12,
    });
  });

  it('throws the bot reason for any other refusal', async () => {
    http.POST.mockResolvedValue({
      error: { message: 'No such script' },
      response: {
        ok: false,
        status: 404,
        headers: new Headers(),
      },
    });

    await expect(runScriptTest('a', { variables: {}, args: [] })).rejects.toThrow('No such script');
  });

  it('gives the editor a failed outcome with the bot message for a rate limit', () => {
    expect(
      toEditorOutcome({
        kind: 'rateLimited',
        message: 'Slow down',
        retryAfterSeconds: null,
      }),
    ).toEqual({ ok: false, message: 'Slow down' });
  });
});

describe('test triggers', () => {
  it('turns each sample into an editor trigger that names the viewer', async () => {
    http.GET.mockResolvedValue({
      data: {
        data: [
          {
            id: 'follow',
            responseKey: 'event.follow',
            userDisplayName: 'Ann',
            variables: { user: 'ann' },
          },
          {
            id: 'cheer',
            responseKey: 'event.cheer',
            userDisplayName: null,
            variables: {},
          },
        ],
      },
      response: ok,
    });

    expect(await fetchTestTriggers()).toEqual([
      {
        id: 'follow',
        label: 'event.follow (Ann)',
        variables: { user: 'ann' },
      },
      {
        id: 'cheer',
        label: 'event.cheer',
        variables: {},
      },
    ]);

    expect(http.GET.mock.calls[0]?.[0]).toBe('/api/v{version}/code-scripts/test-triggers');
  });
});

describe('versions', () => {
  it('lists versions for the editor and marks the published one', async () => {
    http.GET.mockResolvedValue({
      data: {
        data: [
          {
            id: 'v2',
            version: 2,
            validationStatus: 'valid',
          },
          {
            id: 'v1',
            version: '1',
            validationStatus: 'invalid',
          },
        ],
        hasMore: true,
        nextPage: 2,
      },
      response: ok,
    });

    const page = await fetchScriptVersions('a', 'v2', 1);

    expect(page).toEqual({
      versions: [
        {
          id: 'v2',
          version: 2,
          validationStatus: 'valid',
          isCurrent: true,
        },
        {
          id: 'v1',
          version: 1,
          validationStatus: 'invalid',
          isCurrent: false,
        },
      ],
      hasMore: true,
    });

    const [, init] = http.GET.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown>; query: Record<string, unknown> } },
    ];

    expect(init.params.path).toEqual({ version: '1', id: 'a' });
    expect(init.params.query).toMatchObject({ Page: 1 });
  });

  it('publishes one version by script and version id', async () => {
    http.POST.mockResolvedValue({ response: ok });

    await publishScriptVersion('a', 'v1');

    const [path, init] = http.POST.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ];

    expect(path).toBe('/api/v{version}/code-scripts/{id}/versions/{versionId}/publish');

    expect(init.params.path).toEqual({
      version: '1',
      id: 'a',
      versionId: 'v1',
    });
  });

  it('deletes one version and fails with the bot reason when the published one is named', async () => {
    http.DELETE.mockResolvedValueOnce({ response: ok });
    await deleteScriptVersion('a', 'v1');
    const [path, init] = http.DELETE.mock.calls[0] as [
      string,
      { params: { path: Record<string, unknown> } },
    ];

    expect(path).toBe('/api/v{version}/code-scripts/{id}/versions/{versionId}');

    expect(init.params.path).toEqual({
      version: '1',
      id: 'a',
      versionId: 'v1',
    });

    http.DELETE.mockResolvedValueOnce({
      error: { message: 'Publish another version first' },
      response: {
        ok: false,
        status: 400,
        headers: new Headers(),
      },
    });

    await expect(deleteScriptVersion('a', 'v2')).rejects.toThrow('Publish another version first');
  });
});
