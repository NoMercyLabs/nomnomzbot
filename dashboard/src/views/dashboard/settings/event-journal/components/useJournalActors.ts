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

import { useQueries } from '@tanstack/vue-query';
import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import type { JournalEntry } from '@/api/eventJournal';
import { viewerProfileOptions } from '@/api/people';

export type JournalActor = {
  name: string;
  twitchUserId: string | null;
  color: string | null;
};

// A journal row names its actor by id only, so each person on the page is read once (cached) for the name.
// A person whose profile cannot be read has no entry; the row then names where the event came from.
export function useJournalActors(
  channelId: MaybeRefOrGetter<string | null>,
  rows: MaybeRefOrGetter<readonly JournalEntry[]>,
): ComputedRef<Map<string, JournalActor>> {
  const ids = computed(() =>
    [...new Set(toValue(rows).map((row) => row.actorUserId ?? null))]
      .filter((id) => id !== null));

  const profiles = useQueries({
    queries: computed(() => {
      const channel = toValue(channelId);

      if (channel === null)
        return [];

      return ids.value.map((id) => viewerProfileOptions(channel, id));
    }),
  });

  return computed(() => {
    const actors = new Map<string, JournalActor>();

    ids.value.forEach((id, index) => {
      const identity = profiles.value[index]?.data?.identity;

      if (identity === undefined) return;

      actors.set(id, {
        name: identity.displayName,
        twitchUserId: identity.twitchUserId ?? null,
        color: identity.chatColor ?? null,
      });
    });

    return actors;
  });
}
