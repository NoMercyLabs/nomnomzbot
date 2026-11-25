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

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { chosenChannelId, useCurrentChannel, type ChannelChoice } from '../useCurrentChannel';

const waiting = new Map<string, (entered: { id: string }) => void>();
const mutateAsync = vi.fn<(twitchId: string) => Promise<{ id: string }>>(
  (twitchId) =>
    new Promise<{ id: string }>((resolve) => {
      waiting.set(twitchId, resolve);
    }),
);

vi.mock('@/api/channels', () => ({
  moderatedChannelsQuery: {},
  myChannelsQuery: {},
  useMyChannels: () => ({
    data: ref([]),
    isPending: ref(false),
    isError: ref(false),
  }),
  useModeratedChannels: () => ({
    data: ref([]),
    isPending: ref(false),
    isError: ref(false),
  }),
  useEnterModeratedChannel: () => ({
    mutateAsync,
    isPending: ref(false),
  }),
}));

function notSetUp(login: string) {
  return {
    login,
    displayName: login,
    profileImageUrl: null,
    isLive: false,
    kind: 'moderated',
    channelId: null,
    twitchId: `${login}-twitch`,
    chatColor: null,
    suspension: null,
  } as const satisfies ChannelChoice;
}

beforeEach(() => {
  waiting.clear();
  chosenChannelId.value = '';
});

describe('switching channels quickly', () => {
  it('keeps the last pick when the first pick finishes after it', async () => {
    const { choose } = useCurrentChannel();

    const first = choose(notSetUp('first'));
    const second = choose(notSetUp('second'));

    waiting.get('second-twitch')!({ id: 'second-id' });
    await second;
    waiting.get('first-twitch')!({ id: 'first-id' });
    await first;

    expect(chosenChannelId.value).toBe('second-id');
  });
});
