// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
export function useObsClocks(sources: MaybeRefOrGetter<LiveSources>): ObsClocks {
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
  };
}
