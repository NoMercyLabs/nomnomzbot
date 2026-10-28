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

import { beforeEach, describe, expect, it } from 'vitest';
import { nextTick } from 'vue';

import {
  RECENT_CHANNEL_LIMIT,
  useRecentChannels,
  withRecent,
  type RecentChannel,
} from '../useRecentChannels';

function channel(login: string) {
  return {
    login,
    displayName: login.toUpperCase(),
    profileImageUrl: null,
  } as const satisfies RecentChannel;
}

describe('recent channels', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('puts the opened channel first and drops its older copy', () => {
    const list = [channel('a'), channel('fillz'), channel('b')];

    const next = withRecent(list, channel('fillz'));

    expect(next.map((row) => row.login)).toEqual(['fillz', 'a', 'b']);
  });

  it('keeps only the newest channels', () => {
    let list: RecentChannel[] = [];

    for (let index = 0; index < RECENT_CHANNEL_LIMIT + 2; index++) {
      list = withRecent(list, channel(`c${index}`));
    }

    expect(list).toHaveLength(RECENT_CHANNEL_LIMIT);
    expect(list[0]?.login).toBe(`c${RECENT_CHANNEL_LIMIT + 1}`);
  });

  it('remembers a channel across a remount under its own key', async () => {
    useRecentChannels().remember(channel('fillz'));
    await nextTick();

    const stored = localStorage.getItem('nnz.viewerChat.recent') ?? '';

    expect(JSON.parse(stored)).toEqual([channel('fillz')]);
    expect(useRecentChannels().recent.value.map((row) => row.login)).toEqual(['fillz']);
  });
});
