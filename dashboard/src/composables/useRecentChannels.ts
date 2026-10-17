// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage } from '@vueuse/core';
import type { Ref } from 'vue';

import type { PublicChannel } from '@/api/channels';

// The viewer chats a person opened last, on this device (chat-client.md 9a "Reaching any channel").
export type RecentChannel = Pick<PublicChannel, 'login' | 'displayName' | 'profileImageUrl'> &
  Partial<Pick<PublicChannel, 'id' | 'onBot'>>;

export const RECENT_CHANNEL_LIMIT = 5;

const recentChannels = useLocalStorage<RecentChannel[]>('nnz.viewerChat.recent', []);

// The opened channel goes first; its older copy and anything past the limit drop off.
export function withRecent(list: readonly RecentChannel[], entry: RecentChannel): RecentChannel[] {
  const others = list.filter((row) => row.login !== entry.login);

  return [entry, ...others].slice(0, RECENT_CHANNEL_LIMIT);
}

export type RecentChannels = {
  recent: Ref<RecentChannel[]>;
  remember: (entry: RecentChannel) => void;
};

export function useRecentChannels(): RecentChannels {
  function remember(entry: RecentChannel): void {
    recentChannels.value = withRecent(recentChannels.value, {
      login: entry.login,
      displayName: entry.displayName,
      profileImageUrl: entry.profileImageUrl ?? null,
      id: entry.id,
      onBot: entry.onBot,
    });
  }

  return {
    recent: recentChannels,
    remember,
  };
}
