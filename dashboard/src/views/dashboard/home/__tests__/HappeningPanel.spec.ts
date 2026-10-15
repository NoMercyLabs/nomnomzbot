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

import { i18n } from '@/i18n';
import { useProfileDialogStore } from '@/stores/profileDialog';

import HappeningPanel from '../components/HappeningPanel.vue';
import en from '../i18n/en.json';

type Fed = {
  type: string;
  username: string | null;
  userId?: string | null;
  data: unknown;
};

let fed: Fed[] = [];
let allowed = false;
let replayStatus = 200;
let replayResult = {
  eventsReplayed: 1,
  chatMessagesSent: 2,
  ttsQueued: 1,
  overlaysShown: 0,
  widgetsNotified: 3,
};
const post = vi.fn<(...args: unknown[]) => Promise<unknown>>();
const toastSuccess = vi.fn<(message: string) => void>();
const toastError = vi.fn<(message: string) => void>();
const toastWarning = vi.fn<(message: string) => void>();

vi.mock('vue-sonner', () => ({
  toast: {
    success: (message: string) => toastSuccess(message),
    error: (message: string) => toastError(message),
    warning: (message: string) => toastWarning(message),
  },
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => ({
      data: {
        data: path.includes('roles/effective/me')
          ? { heldActionKeys: allowed
              ? ['dashboard:replay']
              : [] }
          : fed.map((item, index) => ({
              id: `e${index}`,
              type: item.type,
              userId: item.userId ?? null,
              username: item.username,
              data: JSON.stringify(item.data),
              timestamp: '2026-10-10T21:15:00',
            })),
      },
      response: { status: 200, ok: true },
    }),
    POST: (...args: unknown[]) => post(...args),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ isLive: true }) }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  allowed = false;
  replayStatus = 200;

  replayResult = {
    eventsReplayed: 1,
    chatMessagesSent: 2,
    ttsQueued: 1,
    overlaysShown: 0,
    widgetsNotified: 3,
  };

  post.mockReset();
  toastSuccess.mockClear();
  toastError.mockClear();
  toastWarning.mockClear();

  post.mockImplementation(async () => ({
    data: { data: replayResult },
    error: replayStatus === 200
      ? undefined
      : { code: 'NOT_FOUND' },
    response: { status: replayStatus, ok: replayStatus === 200 },
  }));

  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

describe('activity row actions', () => {
  const event = {
    type: 'channel.follow',
    username: 'Alice',
    userId: 'twitch-42',
    data: {},
  };

  it('opens the clicked person profile', async () => {
    await rowsFor([event]);
    document.body.querySelector<HTMLButtonElement>('[data-user="twitch-42"]')?.click();
    expect(useProfileDialogStore().userId).toBe('twitch-42');
  });

  it('hides Replay without the action permission', async () => {
    await rowsFor([event]);
    expect(document.body.querySelector('[data-activity-replay]')).toBeNull();
  });

  it('posts the event with no body and reports the actual counts', async () => {
    allowed = true;
    await rowsFor([event]);
    document.body.querySelector<HTMLButtonElement>('[data-activity-replay]')?.click();
    await flushPromises();

    expect(post).toHaveBeenCalledWith(
      '/api/v{version}/dashboard/{channelId}/activity/{eventId}/replay',
      { params: { path: {
        version: '1',
        channelId: 'c1',
        eventId: 'e0',
      } } },
    );

    expect(toastSuccess).toHaveBeenCalledWith(
      'Replayed 1 event: 2 chat messages, 1 voice, 0 overlays, 3 widgets.',
    );
  });

  it('reports NOT_FOUND without claiming a replay', async () => {
    allowed = true;
    replayStatus = 404;
    await rowsFor([event]);
    document.body.querySelector<HTMLButtonElement>('[data-activity-replay]')?.click();
    await flushPromises();
    expect(toastError).toHaveBeenCalledWith('Nothing to replay for this event.');
    expect(toastSuccess).not.toHaveBeenCalled();
  });

  it('warns, not succeeds, when the replay re-performed nothing', async () => {
    allowed = true;
    replayStatus = 409;

    await rowsFor([event]);
    document.body.querySelector<HTMLButtonElement>('[data-activity-replay]')?.click();
    await flushPromises();

    expect(toastWarning).toHaveBeenCalledWith(
      'Found the event, but there was nothing to show again: no response is set up for it and no alert was saved.',
    );

    expect(toastSuccess).not.toHaveBeenCalled();
    expect(toastError).not.toHaveBeenCalled();
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function rowsFor(items: Fed[]): Promise<{ title: string; detail: string }[]> {
  fed = items;

  mounted = mount(HappeningPanel, {
    props: { live: true },
    attachTo: document.body,
    global: {
      stubs: {
        Tooltip: { template: '<div><slot /></div>' },
        TooltipTrigger: { template: '<div><slot /></div>' },
        TooltipContent: true,
      },
      plugins: [
        i18n,
        createRouter({ history: createMemoryHistory(), routes: [] }),
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();

  const nodes = [...document.body.querySelectorAll<HTMLElement>('[data-activity-row]')];

  return nodes.map((node) => ({
    title: node.querySelector('.font-medium')?.textContent ?? '',
    detail: node.querySelector('.font-medium + span')?.textContent ?? '',
  }));
}

describe('HappeningPanel names who', () => {
  it('names the shoutout target as the bold title on every shoutout shape the server writes', async () => {
    const rows = await rowsFor([
      {
        type: 'bot.shoutout.pending',
        username: null,
        data: { Target: { DisplayName: 'Jeroen' } },
      },
      {
        type: 'bot.shoutout.auto-pending',
        username: null,
        data: { DisplayName: 'Kitte' },
      },
      {
        type: 'channel.shoutout.create',
        username: null,
        data: { toDisplayName: 'Aaoa' },
      },
      {
        type: 'channel.shoutout.receive',
        username: null,
        data: { fromBroadcasterDisplayName: 'CptTree', viewerCount: 6 },
      },
    ]);

    expect(rows.map((row) => row.title)).toEqual(['Jeroen', 'Kitte', 'Aaoa', 'CptTree']);
    expect(rows[0]?.detail).toContain('Shoutout waiting');
    expect(rows[2]?.detail).toContain('Shoutout sent');
  });

  it('names the user on a channel event whose server username is empty', async () => {
    const rows = await rowsFor([
      {
        type: 'channel.ban',
        username: null,
        data: { targetDisplayName: 'Troll' },
      },
      {
        type: 'channel.subscription.gift',
        username: null,
        data: { gifterDisplayName: 'Gifter' },
      },
      {
        type: 'channel.follow',
        username: null,
        data: { userDisplayName: 'StanLeFresh' },
      },
    ]);

    expect(rows.map((row) => row.title)).toEqual(['Troll', 'Gifter', 'StanLeFresh']);
    expect(rows[2]?.detail).toContain('Followed');
  });

  it('keeps the generic title for an event that carries no name at all', async () => {
    const rows = await rowsFor([
      {
        type: 'channel.hype_train.begin',
        username: null,
        data: { level: 1 },
      },
    ]);

    expect(rows[0]?.title).toBe('Hype train started');
  });
});
