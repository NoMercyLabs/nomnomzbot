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

import { useLocalStorage } from '@vueuse/core';
import { computed, type ComputedRef } from 'vue';

import {
  moderatedChannelsQuery,
  myChannelsQuery,
  useEnterModeratedChannel,
  useModeratedChannels,
  useMyChannels,
  type ChannelSummary,
  type ChannelSuspension,
  type ModeratedChannel,
} from '@/api/channels';
import { ensureQuery } from '@/api/queryClient';
import { merge } from '@/lib/array';

export type ChannelChoice = {
  login: string;
  displayName: string;
  profileImageUrl: string | null;
  // Null when the bot has no live state for the channel (a Twitch channel the person moderates, not set up here yet).
  isLive: boolean | null;
  kind: 'own' | 'moderated';
  // Null until the channel is set up in this bot; choosing it sets it up in moderator mode first.
  channelId: string | null;
  twitchId: string | null;
  // The streamer's chat color (#RRGGBB), the theme accent while this channel is managed.
  chatColor: string | null;
  // Set while the platform has suspended the channel: the frame then shows why and how to reach the platform.
  suspension: ChannelSuspension | null;
};

export type CurrentChannel = {
  channels: ComputedRef<readonly ChannelChoice[]>;
  current: ComputedRef<ChannelChoice | null>;
  channelId: ComputedRef<string | null>;
  isPending: ComputedRef<boolean>;
  // Both channel lists have answered, so an absent channel is truly absent.
  isLoaded: ComputedRef<boolean>;
  choose: (choice: ChannelChoice) => Promise<void>;
};

// The channel being managed. It outlives a reload and is shared by every part of the frame.
export const chosenChannelId = useLocalStorage<string>('nnz.channel', '');

// The channels the person reaches: the ones set up here, then the Twitch channels they moderate that are not.
export function channelChoices(
  rows: readonly ChannelSummary[],
  moderatedRows: readonly ModeratedChannel[],
): readonly ChannelChoice[] {
  const own = rows.map((row) => ({
    login: row.login,
    displayName: row.displayName,
    profileImageUrl: row.profileImageUrl ?? null,
    isLive: row.isLive,
    kind: row.role === 'broadcaster'
      ? 'own'
      : 'moderated',
    channelId: row.id,
    twitchId: null,
    chatColor: row.chatColor ?? null,
    suspension: row.suspension ?? null,
  } as const satisfies ChannelChoice));
  const known = new Set<string>(own.map((row) => row.login.toLowerCase()));
  const notSetUp = moderatedRows
    .filter((row) => !known.has(row.login.toLowerCase()))
    .map((row) => ({
      login: row.login,
      displayName: row.displayName,
      profileImageUrl: null,
      isLive: null,
      kind: 'moderated',
      channelId: null,
      twitchId: row.id,
      chatColor: null,
      suspension: null,
    } as const satisfies ChannelChoice));

  return merge(own, notSetUp);
}

// The chosen channel, else the person's own, else any that is set up.
export function pickCurrent(
  choices: readonly ChannelChoice[],
  chosenId: string,
): ChannelChoice | null {
  return (
    choices.find((row) => row.channelId !== null && row.channelId === chosenId) ??
    choices.find((row) => row.kind === 'own') ??
    choices.find((row) => row.channelId !== null) ??
    null
  );
}

// The login of the channel being managed, read outside a component (the router waits for it). Null for a person with no channel.
// A failed list is not retried here: a navigation must not wait out the retry back-off of a list that is down.
export async function currentChannelLogin(): Promise<string | null> {
  const [mine, moderated] = await Promise.all([
    ensureQuery(myChannelsQuery),
    ensureQuery(moderatedChannelsQuery),
  ]);

  return pickCurrent(channelChoices(mine, moderated), chosenChannelId.value)?.login ?? null;
}

export function useCurrentChannel(): CurrentChannel {
  const mine = useMyChannels();
  const moderated = useModeratedChannels();
  const enter = useEnterModeratedChannel();

  const channels = computed(() =>
    channelChoices(mine.data.value ?? [], moderated.data.value ?? []),
  );
  const current = computed(() => pickCurrent(channels.value, chosenChannelId.value));

  async function choose(choice: ChannelChoice): Promise<void> {
    if (choice.channelId) {
      chosenChannelId.value = choice.channelId;

      return;
    }

    if (!choice.twitchId) return;

    const entered = await enter.mutateAsync(choice.twitchId);

    if (entered) {
      chosenChannelId.value = entered.id;
    }
  }

  return {
    channels,
    current,
    channelId: computed(() => current.value?.channelId ?? null),
    isPending: computed(() => enter.isPending.value),
    // A failed list is not a loaded one: a refused request must never read as "no access to this channel".
    isLoaded: computed(
      () =>
        !mine.isPending.value &&
        !moderated.isPending.value &&
        !mine.isError.value &&
        !moderated.isError.value,
    ),
    choose,
  } as const satisfies CurrentChannel;
}
