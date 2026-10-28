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

import { useCurrentChannel } from '@/composables/useCurrentChannel';

const mutateAsync = vi.fn<(twitchId: string) => Promise<{ id: string } | null>>();
const moderatedFailed = vi.hoisted(() => ({ value: false }));

vi.mock('@/api/channels', () => ({
  useMyChannels: () => ({
    isPending: ref(false),
    isError: ref(false),
    data: ref([
      {
        id: 'own-id',
        login: 'streamer',
        displayName: 'Streamer',
        profileImageUrl: null,
        isLive: true,
        role: 'broadcaster',
      },
      {
        id: 'mod-id',
        login: 'friend',
        displayName: 'Friend',
        profileImageUrl: null,
        isLive: false,
        role: 'moderator',
      },
    ]),
  }),
  useModeratedChannels: () => ({
    isPending: ref(false),
    isError: ref(moderatedFailed.value),
    data: ref([
      {
        id: 'tw-1',
        login: 'friend',
        displayName: 'Friend',
        isOnboarded: true,
      },
      {
        id: 'tw-2',
        login: 'newplace',
        displayName: 'NewPlace',
        isOnboarded: false,
      },
    ]),
  }),
  useEnterModeratedChannel: () => ({ mutateAsync, isPending: ref(false) }),
}));

beforeEach(() => {
  localStorage.clear();
  mutateAsync.mockReset();
  moderatedFailed.value = false;
});

describe('the channel being managed', () => {
  it('is loaded when both lists answered', () => {
    expect(useCurrentChannel().isLoaded.value).toBe(true);
  });

  it('is not loaded when a list failed, so a refused request never reads as no access', () => {
    moderatedFailed.value = true;

    expect(useCurrentChannel().isLoaded.value).toBe(false);
  });

  it('lists the own channel, the set-up moderated channel once, and the one not set up yet', () => {
    const channels = useCurrentChannel();

    expect(channels.channels.value.map((row) => [row.login, row.kind, row.channelId])).toEqual([
      ['streamer', 'own', 'own-id'],
      ['friend', 'moderated', 'mod-id'],
      ['newplace', 'moderated', null],
    ]);
  });

  it('writes the chosen channel to nnz.channel and makes it the current one', async () => {
    const channels = useCurrentChannel();

    expect(channels.channelId.value).toBe('own-id');

    const friend = channels.channels.value[1]!;

    await channels.choose(friend);

    expect(localStorage.getItem('nnz.channel')).toBe('mod-id');
    expect(channels.current.value?.login).toBe('friend');
    expect(channels.channelId.value).toBe('mod-id');
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it('sets a channel up in moderator mode first, then chooses the id it got back', async () => {
    mutateAsync.mockResolvedValue({ id: 'fresh-id' });
    const channels = useCurrentChannel();

    await channels.choose(channels.channels.value[2]!);

    expect(mutateAsync).toHaveBeenCalledWith('tw-2');
    expect(localStorage.getItem('nnz.channel')).toBe('fresh-id');
  });
});
