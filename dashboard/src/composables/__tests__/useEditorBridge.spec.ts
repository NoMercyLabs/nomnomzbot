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

import { describe, expect, it, vi, type Mock } from 'vitest';
import { reactive } from 'vue';

import {
  createEditorBridge,
  type EditorBridge,
  type EditorBridgeOptions,
  type EditorHistoryRunner,
  type EditorOpenPayload,
  type EditorOutcome,
  type VersionsPage,
} from '../useEditorBridge';

const ORIGIN: string = 'http://localhost:5090';

const payload: EditorOpenPayload = {
  title: 'My script',
  files: { 'src/main.ts': 'nnz.log("hi")' },
  entry: 'src/main.ts',
  language: 'script',
  sdkTypes: 'declare const nnz: unknown',
  sdkTypesUnavailable: false,
  labels: { close: 'Close' },
};

type Posted = { message: Record<string, unknown>; origin: string };

// The editor frame's window of the latest setup: a page message comes from it unless a test says otherwise.
let frameWindow: Pick<Window, 'postMessage'> | null = null;

function setup(overrides: Partial<EditorBridgeOptions> = {}) {
  const posted: Posted[] = [];
  const closed = vi.fn<() => void>();
  const target = {
    postMessage: ((message: Record<string, unknown>, origin: string) => {
      posted.push({ message, origin });
    }) as Window['postMessage'],
  } as const satisfies Pick<Window, 'postMessage'>;

  frameWindow = target;
  const bridge: EditorBridge = createEditorBridge({
    origin: ORIGIN,
    target: () => target,
    payload: () => payload,
    onSave: async () => ({ ok: true, message: 'Saved' }),
    onClose: closed,
    ...overrides,
  });

  return {
    bridge,
    posted,
    closed,
  } as const satisfies {
    bridge: EditorBridge;
    posted: Posted[];
    closed: Mock<() => void>;
  };
}

function fromPage(
  data: Record<string, unknown>,
  origin: string = ORIGIN,
  source: unknown = frameWindow,
): MessageEvent {
  const event: MessageEvent = new MessageEvent('message', { data, origin });

  // jsdom accepts only a real window as source, so the fake frame window is set afterwards.
  Object.defineProperty(event, 'source', { value: source });

  return event;
}

describe('createEditorBridge', () => {
  it('posts data from reactive state that structuredClone accepts, with the same content', async () => {
    const clones: unknown[] = [];
    const target = {
      postMessage: ((message: unknown) => {
        clones.push(structuredClone(message));
      }) as Window['postMessage'],
    } as const satisfies Pick<Window, 'postMessage'>;
    const state = reactive({
      ...payload,
      files: { 'src/main.ts': 'nnz.log("hi")' },
      labels: { close: 'Close' },
    });

    frameWindow = target;

    const bridge: EditorBridge = createEditorBridge({
      origin: ORIGIN,
      target: () => target,
      payload: () => state,
      onSave: async () => ({ ok: true, message: 'Saved' }),
      onClose: vi.fn<() => void>(),
    });

    await bridge.handle(fromPage({ type: 'nnz:editor:ready' }));
    expect(clones).toHaveLength(1);

    expect(clones[0]).toEqual({
      type: 'nnz:editor:open',
      payload: {
        ...payload,
        fireSamples: {},
        eventSubscriptions: [],
        testRunEnabled: false,
        testTriggers: [],
      },
    });
  });

  it('answers ready with exactly one open that carries the payload, to the same origin', async () => {
    const { bridge, posted } = setup();

    await bridge.handle(fromPage({ type: 'nnz:editor:ready' }));
    expect(posted).toHaveLength(1);
    expect(posted[0]?.origin).toBe(ORIGIN);

    expect(posted[0]?.message).toEqual({
      type: 'nnz:editor:open',
      payload: {
        ...payload,
        fireSamples: {},
        eventSubscriptions: [],
        testRunEnabled: false,
        testTriggers: [],
      },
    });
  });

  it('hands a save to onSave and replies compiled with its errors', async () => {
    const onSave = vi.fn<EditorBridgeOptions['onSave']>(async () => ({
      ok: false,
      message: 'Build failed',
      errors: [
        {
          message: 'Unexpected token',
          file: 'src/main.ts',
          line: 3,
          column: 7,
        },
      ],
    }));
    const { bridge, posted } = setup({ onSave });

    await bridge.handle(fromPage({ type: 'nnz:editor:save', files: { 'src/main.ts': 'x' } }));
    expect(onSave).toHaveBeenCalledExactlyOnceWith({ 'src/main.ts': 'x' });
    expect(posted).toHaveLength(1);

    expect(posted[0]?.message).toEqual({
      type: 'nnz:editor:compiled',
      ok: false,
      message: 'Build failed',
      errors: [
        {
          message: 'Unexpected token',
          file: 'src/main.ts',
          line: 3,
          column: 7,
        },
      ],
    });
  });

  it('replies to a failed save with a compiled failure instead of staying silent', async () => {
    const { bridge, posted } = setup({
      onSave: async () => {
        throw new Error('network down');
      },
    });

    await bridge.handle(fromPage({ type: 'nnz:editor:save', files: {} }));

    expect(posted[0]?.message).toMatchObject({
      type: 'nnz:editor:compiled',
      ok: false,
      message: 'network down',
    });
  });

  it('ignores a message of the same origin that another window sent', async () => {
    const onSave = vi.fn<EditorBridgeOptions['onSave']>(async () => ({
      ok: true,
      message: 'Saved',
    }));
    const { bridge, posted, closed } = setup({ onSave });
    const otherWindow = {
      postMessage: (() => {}) as Window['postMessage'],
    } as const satisfies Pick<Window, 'postMessage'>;

    await bridge.handle(fromPage({ type: 'nnz:editor:ready' }, ORIGIN, otherWindow));
    await bridge.handle(fromPage({ type: 'nnz:editor:save', files: {} }, ORIGIN, otherWindow));
    await bridge.handle(fromPage({ type: 'nnz:editor:close' }, ORIGIN, otherWindow));
    expect(posted).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
    expect(closed).not.toHaveBeenCalled();
  });

  it('ignores a message from another origin', async () => {
    const onSave = vi.fn<EditorBridgeOptions['onSave']>(async () => ({
      ok: true,
      message: 'Saved',
    }));
    const { bridge, posted, closed } = setup({ onSave });

    await bridge.handle(fromPage({ type: 'nnz:editor:ready' }, 'https://evil.example'));
    await bridge.handle(fromPage({ type: 'nnz:editor:save', files: {} }, 'https://evil.example'));
    await bridge.handle(fromPage({ type: 'nnz:editor:close' }, 'https://evil.example'));
    expect(posted).toEqual([]);
    expect(onSave).not.toHaveBeenCalled();
    expect(closed).not.toHaveBeenCalled();
  });

  it('ignores traffic that is not an editor page message', async () => {
    const { bridge, posted } = setup();

    await bridge.handle(fromPage({ type: 'nnz:editor:open' }));
    await bridge.handle(fromPage({ type: 'something-else' }));
    expect(posted).toEqual([]);
  });

  it('replies historyError for every history request when no history runner is given', async () => {
    const { bridge, posted } = setup();

    await bridge.handle(fromPage({ type: 'nnz:editor:historyLoadMore' }));
    await bridge.handle(fromPage({ type: 'nnz:editor:historyRollback', versionId: 'v1' }));
    await bridge.handle(fromPage({ type: 'nnz:editor:historyDelete', versionId: 'v1' }));

    expect(posted.map((entry) => entry.message.type)).toEqual([
      'nnz:editor:historyError',
      'nnz:editor:historyError',
      'nnz:editor:historyError',
    ]);
  });

  it('passes the version id to rollback and replies with the page it returns', async () => {
    const page = {
      versions: [
        {
          id: 'v2',
          version: 2,
          validationStatus: 'valid',
          isCurrent: true,
        },
      ],
      hasMore: false,
    };
    const rollback = vi.fn<EditorHistoryRunner['rollback']>(async () => ({
      ok: true,
      value: page,
    }));
    const unused = async (): Promise<EditorOutcome<VersionsPage>> => ({
      ok: false,
      message: 'unused',
    });
    const { bridge, posted } = setup({
      history: {
        loadMore: unused,
        rollback,
        remove: unused,
      },
    });

    await bridge.handle(fromPage({ type: 'nnz:editor:historyRollback', versionId: 'v2' }));
    expect(rollback).toHaveBeenCalledExactlyOnceWith('v2');
    expect(posted[0]?.message).toEqual({ type: 'nnz:editor:historyPage', payload: page });
  });

  it('replies a failed test run and a failed preview action when no runner is given', async () => {
    const { bridge, posted } = setup();

    await bridge.handle(
      fromPage({
        type: 'nnz:editor:testRun',
        files: {},
        variables: {},
        args: [],
      }),
    );

    await bridge.handle(
      fromPage({
        type: 'nnz:editor:previewAction',
        requestId: 'r1',
        actionType: 'tts_synthesize',
        params: {},
        variables: {},
      }),
    );

    expect(posted[0]?.message).toMatchObject({ type: 'nnz:editor:testRunResult', ok: false });

    expect(posted[1]?.message).toMatchObject({
      type: 'nnz:editor:previewActionResult',
      requestId: 'r1',
      success: false,
      errorCode: 'PREVIEW_ACTION_UNAVAILABLE',
    });
  });

  it('calls onClose for a close and posts nothing', async () => {
    const { bridge, posted, closed } = setup();

    await bridge.handle(fromPage({ type: 'nnz:editor:close' }));
    expect(closed).toHaveBeenCalledOnce();
    expect(posted).toEqual([]);
  });

  it('asks the page to close with requestClose', () => {
    const { bridge, posted } = setup();

    bridge.requestClose();
    expect(posted).toEqual([{ message: { type: 'nnz:editor:requestClose' }, origin: ORIGIN }]);
  });
});
