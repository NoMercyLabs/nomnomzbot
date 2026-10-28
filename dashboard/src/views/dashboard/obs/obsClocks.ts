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

import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { useLiveTimer } from '@/composables/useLiveTimer';

import { liveOutputs, type LiveOutputs, type LiveSources } from './obsStatusFacts';

export type ObsClocks = {
  live: ComputedRef<LiveOutputs | null>;
  stream: ComputedRef<string>;
  record: ComputedRef<string>;
};

// The stream and recording timers count every second on their own from the newest timecode, a poll or a push.
export function useObsClocks(sources: MaybeRefOrGetter<LiveSources>) {
  const live = computed<LiveOutputs | null>(() => liveOutputs(toValue(sources)));

  return {
    live,
    stream: useLiveTimer(
      () => live.value?.streamTimecode,
      () => live.value?.streaming === true,
    ),
    record: useLiveTimer(
      () => live.value?.recordTimecode,
      () => live.value?.recording === true,
      () => live.value?.paused !== true,
    ),
  } as const satisfies ObsClocks;
}
