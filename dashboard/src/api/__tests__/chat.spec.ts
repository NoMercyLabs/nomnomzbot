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
import { createApp, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
  chatKey,
  useBanUser,
  useChatHistory,
  useDeleteChatMessage,
  useSendChat,
  useTimeoutUser,
  useUnbanUser,
  type ChatLine,
} from '../chat';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Sent = [
  string,
  { params: { path: Record<string, unknown>; query?: unknown }; body?: unknown },
];

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('../client', () => ({ client: http }));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const channel = 'c1';

const line = (id: string, timestamp: string = '2026-10-14T12:00:00Z'): ChatLine =>
  ({
    id,
    channelId: channel,
    message: id,
    timestamp,
  }) as ChatLine;

function mount<T>(use: () => T): { queries: QueryClient; hook: T } {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  let hook!: T;
  const app: App = createApp({
    setup() {
      hook = use();

      return () => null;
    },
  });

  app.use(VueQueryPlugin, { queryClient: queries });
  app.mount(document.createElement('div'));

  return { queries, hook };
}

const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

beforeEach(() => {
  http.GET.mockReset();
  http.POST.mockReset();
  http.DELETE.mockReset();
  http.POST.mockResolvedValue({ response: ok });
  http.DELETE.mockResolvedValue({ response: ok });
});

describe('useChatHistory', () => {
  it('seeds the channel feed from the history call and keeps live lines that arrived meanwhile without a duplicate', async () => {
    let finish: (answer: Answer) => void = () => {};

    http.GET.mockReturnValue(new Promise<Answer>((done) => (finish = done)));
    const { queries } = mount(() => useChatHistory(channel));

    await settle();
    queries.setQueryData(chatKey(channel), [line('b'), line('c')]);

    finish({ data: { data: [line('a'), line('b')] }, response: ok });
    await settle();

    const [path, init] = http.GET.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/chat/messages');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });

    expect(queries.getQueryData<ChatLine[]>(chatKey(channel))?.map((entry) => entry.id)).toEqual([
      'a',
      'b',
      'c',
    ]);
  });

  // Owner 2026-10-14: "where do these come from? they are not from now". After a restart the history came
  // back as the newest lines only; older cached lines were appended under today's chat.
  it('puts older cached lines above a refetched history, never under it', async () => {
    let finish: (answer: Answer) => void = () => {};

    http.GET.mockReturnValue(new Promise<Answer>((done) => (finish = done)));
    const { queries } = mount(() => useChatHistory(channel));

    await settle();

    queries.setQueryData(chatKey(channel), [
      line('old', '2026-10-09T20:23:00Z'),
      line('today', '2026-10-14T17:59:00Z'),
    ]);

    finish({ data: { data: [line('today', '2026-10-14T17:59:00Z'), line('now', '2026-10-14T18:01:00Z')] }, response: ok });
    await settle();

    expect(queries.getQueryData<ChatLine[]>(chatKey(channel))?.map((entry) => entry.id)).toEqual([
      'old',
      'today',
      'now',
    ]);
  });
});

describe('useSendChat', () => {
  it('posts the message with the sender identity and the reply target', async () => {
    const { hook } = mount(() => useSendChat(channel));

    await hook.mutateAsync({
      message: 'hello',
      senderIdentity: 'bot',
      replyToMessageId: 'm1',
    });

    const [path, init] = http.POST.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/chat/messages');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });

    expect(init.body).toEqual({
      message: 'hello',
      senderIdentity: 'bot',
      replyToMessageId: 'm1',
    });
  });

  it('sends as "you" by default', async () => {
    const { hook } = mount(() => useSendChat(channel));

    await hook.mutateAsync({ message: 'hi' });

    expect((http.POST.mock.calls[0] as Sent)[1].body).toEqual({
      message: 'hi',
      senderIdentity: 'you',
      replyToMessageId: null,
    });
  });

  it('refuses a message over 500 characters and sends nothing', async () => {
    const { hook } = mount(() => useSendChat(channel));

    await expect(hook.mutateAsync({ message: 'x'.repeat(501) })).rejects.toThrow('too long');
    expect(http.POST).not.toHaveBeenCalled();
  });

  it('fails when the server refuses', async () => {
    http.POST.mockResolvedValue({ response: { ok: false, status: 403 } });
    const { hook } = mount(() => useSendChat(channel));

    await expect(hook.mutateAsync({ message: 'hi' })).rejects.toThrow('send 403');
  });
});

describe('moderation hooks', () => {
  it('delete calls DELETE with the message id', async () => {
    const { hook } = mount(() => useDeleteChatMessage(channel));

    await hook.mutateAsync('m9');

    const [path, init] = http.DELETE.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/chat/messages/{messageId}');

    expect(init.params.path).toEqual({
      version: '1',
      channelId: channel,
      messageId: 'm9',
    });
  });

  it('timeout posts the action "timeout" with the duration and the target id', async () => {
    const { hook } = mount(() => useTimeoutUser(channel));

    await hook.mutateAsync({ targetUserId: 'u7', durationSeconds: 600 });

    const [path, init] = http.POST.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/moderation/actions');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });

    expect(init.body).toEqual({
      action: 'timeout',
      targetUserId: 'u7',
      durationSeconds: 600,
      reason: null,
    });
  });

  it('ban posts the target with its scope', async () => {
    const { hook } = mount(() => useBanUser(channel));

    await hook.mutateAsync({
      targetUserId: 'u7',
      scope: 'all_moderated',
      reason: 'spam',
    });

    const [path, init] = http.POST.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/ban');
    expect(init.params.path).toEqual({ version: '1', channelId: channel });

    expect(init.body).toEqual({
      targetTwitchUserId: 'u7',
      scope: 'all_moderated',
      reason: 'spam',
    });
  });

  it('unban posts the target with its scope', async () => {
    const { hook } = mount(() => useUnbanUser(channel));

    await hook.mutateAsync({ targetUserId: 'u7', scope: 'this_channel' });

    const [path, init] = http.POST.mock.calls[0] as Sent;

    expect(path).toBe('/api/v{version}/channels/{channelId}/moderation/actions/unban');
    expect(init.body).toEqual({ targetTwitchUserId: 'u7', scope: 'this_channel' });
  });
});
