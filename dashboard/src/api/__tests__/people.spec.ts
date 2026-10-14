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
import { createApp, ref, type App } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { useSaveErrorsStore } from '@/stores/saveErrors';

import {
  communityListKey,
  useAddNote,
  useCommunityList,
  useLiftBan,
  useShoutout,
  useTimeout,
  useUnban,
  useUserNotes,
  useViewerProfile,
  userNotesKey,
  viewerProfileKey,
  type UserNote,
} from '../people';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};
type Call = [
  path: string,
  init: {
    params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
    body?: unknown;
  },
];

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('../client', () => ({ client: http }));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];
const channel = 'c1';
const settle = (): Promise<void> => new Promise((done) => setTimeout(done, 0));

const person = (id: string): Record<string, unknown> => ({
  id,
  internalUserId: null,
  username: id,
  displayName: id,
  profileImageUrl: null,
  messageCount: 1,
  watchHours: 1,
  commandsUsed: 0,
  trustLevel: 'viewer',
  isBanned: false,
  firstSeen: '2026-01-01T00:00:00Z',
  lastSeen: '2026-01-02T00:00:00Z',
});

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
  http.PUT.mockReset();
  http.DELETE.mockReset();
});

describe('useCommunityList', () => {
  it('asks for the moderator role on the Moderators tab and omits role on All', async () => {
    http.GET.mockResolvedValue({
      data: {
        data: [person('a')],
        hasMore: false,
        total: 1,
      },
      response: ok,
    });

    const role = ref('moderator');
    const { hook } = mount(() => useCommunityList(channel, role, ref('')));

    await settle();
    expect((http.GET.mock.calls[0] as Call)[1].params?.query?.role).toBe('moderator');

    role.value = 'all';
    await settle();
    const last = http.GET.mock.calls[http.GET.mock.calls.length - 1] as Call;

    expect(last[1].params?.query).not.toHaveProperty('role');
    expect(hook.data.value?.pages[0]?.rows.map((r) => r.id)).toEqual(['a']);
  });

  it('appends the next page while hasMore is true and stops after the last one', async () => {
    http.GET.mockResolvedValueOnce({
      data: {
        data: [person('a')],
        hasMore: true,
        nextPage: 2,
        total: 2,
      },
      response: ok,
    });

    http.GET.mockResolvedValueOnce({
      data: {
        data: [person('b')],
        hasMore: false,
        nextPage: null,
        total: 2,
      },
      response: ok,
    });

    const { hook } = mount(() => useCommunityList(channel, ref('all'), ref('')));

    await settle();
    expect(hook.hasNextPage.value).toBe(true);

    await hook.fetchNextPage();
    await settle();
    expect((http.GET.mock.calls[1] as Call)[1].params?.query?.Page).toBe(2);
    expect(hook.data.value?.pages.flatMap((p) => p.rows.map((r) => r.id))).toEqual(['a', 'b']);
    expect(hook.data.value?.pages[0]?.total).toBe(2);
    expect(hook.hasNextPage.value).toBe(false);
  });

  it('sends the search text and a refusal becomes an error state, not an empty list', async () => {
    http.GET.mockResolvedValue({
      error: { status: 'error' },
      response: { ok: false, status: 403 },
    });

    const { hook } = mount(() => useCommunityList(channel, ref('all'), ref('ste')));

    await settle();
    expect((http.GET.mock.calls[0] as Call)[1].params?.query?.Search).toBe('ste');
    expect(hook.isError.value).toBe(true);
    expect(hook.error.value?.message).toBe('community list 403');
    expect(hook.data.value).toBeUndefined();
  });
});

describe('useViewerProfile', () => {
  const summary = {
    identity: {
      userId: 'g1',
      twitchUserId: 't1',
      username: 'x',
      displayName: 'X',
      profileImageUrl: null,
      pronoun: null,
      altPronoun: null,
      communityStanding: null,
      subTier: null,
      managementRole: null,
      memberSinceUtc: null,
      firstSeenUtc: null,
    },
    trust: {
      trustScore: 91,
      heatScore: 7,
      computedAt: '2026-01-01T00:00:00Z',
    },
    moderationHistory: {
      timeoutCount: 2,
      banCount: 0,
      warningCount: 1,
      messagesDeletedCount: 3,
      lastActionAt: null,
      lastActionType: null,
    },
    activity: null,
    streak: null,
  };

  it('reads the profile by the internal id and never exposes the trust or heat score', async () => {
    http.GET.mockResolvedValue({ data: { data: summary }, response: ok });
    const { hook } = mount(() => useViewerProfile(channel, 'g1'));

    await settle();
    expect((http.GET.mock.calls[0] as Call)[1].params?.path?.userId).toBe('g1');
    expect(hook.data.value?.moderationHistory?.timeoutCount).toBe(2);
    expect(hook.data.value).not.toHaveProperty('trust');
    expect(JSON.stringify(hook.data.value)).not.toContain('heatScore');
  });

  it('does not call the profile endpoint when the person has no internal id', async () => {
    const { hook } = mount(() => useViewerProfile(channel, null));

    await settle();
    expect(http.GET).not.toHaveBeenCalled();
    expect(hook.fetchStatus.value).toBe('idle');
  });

  it('a refusal is an error state', async () => {
    http.GET.mockResolvedValue({ error: {}, response: { ok: false, status: 403 } });
    const { hook } = mount(() => useViewerProfile(channel, 'g1'));

    await settle();
    expect(hook.error.value?.message).toBe('viewer profile 403');
  });
});

describe('notes', () => {
  const note = {
    id: 1,
    subjectUserId: 't1',
    content: 'first',
    pinned: false,
    authorName: 'mod',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  } as const satisfies UserNote;

  it('lists the notes for the Twitch id', async () => {
    http.GET.mockResolvedValue({ data: { data: [note] }, response: ok });
    const { hook } = mount(() => useUserNotes(channel, 't1'));

    await settle();
    expect((http.GET.mock.calls[0] as Call)[1].params?.path?.userId).toBe('t1');
    expect(hook.data.value).toEqual([note]);
  });

  it('posts {content, pinned}, shows the note at once, then holds the list the bot returns', async () => {
    const saved = {
      ...note,
      id: 2,
      content: 'new note',
      pinned: true,
    } as const satisfies UserNote;

    http.GET.mockResolvedValueOnce({ data: { data: [note] }, response: ok });
    http.GET.mockResolvedValueOnce({ data: { data: [saved, note] }, response: ok });
    let release!: () => void;

    http.POST.mockReturnValue(
      new Promise<Answer>((done) => {
        release = (): void => done({ data: { data: saved }, response: ok });
      }),
    );

    const { queries, hook } = mount(() => ({
      list: useUserNotes(channel, 't1'),
      add: useAddNote(channel),
    }));

    await settle();

    const done: Promise<unknown> = hook.add.mutateAsync({
      userId: 't1',
      content: 'new note',
      pinned: true,
    });

    await vi.waitFor(() =>
      expect((queries.getQueryData(userNotesKey(channel, 't1')) as UserNote[])[0]?.content).toBe(
        'new note',
      ),
    );

    expect((queries.getQueryData(userNotesKey(channel, 't1')) as UserNote[])[0]?.id).toBeLessThan(
      0,
    );

    release();
    await done;
    await settle();

    const call = http.POST.mock.calls[0] as Call;

    expect(call[0]).toContain('/moderation/users/{userId}/notes');
    expect(call[1].body).toEqual({ content: 'new note', pinned: true });

    expect(
      (queries.getQueryData(userNotesKey(channel, 't1')) as UserNote[]).map((n) => n.id),
    ).toEqual([2, 1]);
  });

  it('a refused note rolls back and reaches the error inbox', async () => {
    http.GET.mockResolvedValue({ data: { data: [note] }, response: ok });
    http.POST.mockResolvedValue({ error: {}, response: { ok: false, status: 403 } });
    const { queries, hook, inbox } = mount(() => ({
      list: useUserNotes(channel, 't1'),
      add: useAddNote(channel),
    }));

    await settle();

    await expect(
      hook.add.mutateAsync({
        userId: 't1',
        content: 'x',
        pinned: false,
      }),
    ).rejects.toThrow('add note 403');

    expect(queries.getQueryData(userNotesKey(channel, 't1'))).toEqual([note]);
    expect(inbox.items).toHaveLength(1);
  });
});

describe('actions', () => {
  it('shoutout posts the Twitch id and returns the path the bot took', async () => {
    http.POST.mockResolvedValue({
      data: { data: { path: 'helix' } },
      response: { ok: true, status: 202 },
    });

    const { hook } = mount(() => useShoutout(channel));
    const path: string = await hook.mutateAsync({ targetTwitchUserId: 't1' });

    expect((http.POST.mock.calls[0] as Call)[0]).toContain('/moderation/shoutout');
    expect((http.POST.mock.calls[0] as Call)[1].body).toEqual({ targetTwitchUserId: 't1' });
    expect(path).toBe('helix');
  });

  it('timeout 10m sends action timeout with 600 seconds', async () => {
    http.POST.mockResolvedValue({ data: { data: { success: true, message: null } }, response: ok });
    const { hook } = mount(() => useTimeout(channel));

    await hook.mutateAsync({ userId: 't1', durationSeconds: 600 });
    expect((http.POST.mock.calls[0] as Call)[0]).toContain('/moderation/actions');

    expect((http.POST.mock.calls[0] as Call)[1].body).toEqual({
      action: 'timeout',
      targetUserId: 't1',
      durationSeconds: 600,
    });
  });

  it('a timeout the bot reports as unsuccessful is an error carrying its message', async () => {
    http.POST.mockResolvedValue({
      data: { data: { success: false, message: 'not a moderator' } },
      response: ok,
    });

    const { hook } = mount(() => useTimeout(channel));

    await expect(hook.mutateAsync({ userId: 't1', durationSeconds: 60 })).rejects.toThrow(
      'not a moderator',
    );
  });

  it('unban sends the Twitch id with scope this_channel', async () => {
    http.POST.mockResolvedValue({ data: { data: {} }, response: ok });
    const { hook } = mount(() => useUnban(channel));

    await hook.mutateAsync({ userId: 't1' });
    expect((http.POST.mock.calls[0] as Call)[0]).toContain('/moderation/actions/unban');

    expect((http.POST.mock.calls[0] as Call)[1].body).toEqual({
      targetTwitchUserId: 't1',
      scope: 'this_channel',
    });
  });

  it('lifting a ban calls DELETE on the member ban path, and a refusal throws', async () => {
    http.DELETE.mockResolvedValueOnce({ response: { ok: true, status: 204 } });
    const { hook } = mount(() => useLiftBan(channel));

    await hook.mutateAsync({ userId: 't1' });
    const call = http.DELETE.mock.calls[0] as Call;

    expect(call[0]).toContain('/community/{userId}/ban');
    expect(call[1].params?.path?.userId).toBe('t1');

    http.DELETE.mockResolvedValueOnce({ error: {}, response: { ok: false, status: 403 } });
    await expect(hook.mutateAsync({ userId: 't1' })).rejects.toThrow('lift ban 403');
  });

  it('a moderation action marks the member, the profiles and the lists stale', async () => {
    http.POST.mockResolvedValue({ data: { data: { success: true, message: null } }, response: ok });
    const { queries, hook } = mount(() => useTimeout(channel));

    queries.setQueryData(communityListKey(channel, 'all', ''), { pages: [], pageParams: [] });
    queries.setQueryData(viewerProfileKey(channel, 'g1'), {});
    await hook.mutateAsync({ userId: 't1', durationSeconds: 60 });
    expect(queries.getQueryState(communityListKey(channel, 'all', ''))?.isInvalidated).toBe(true);
    expect(queries.getQueryState(viewerProfileKey(channel, 'g1'))?.isInvalidated).toBe(true);
  });
});
