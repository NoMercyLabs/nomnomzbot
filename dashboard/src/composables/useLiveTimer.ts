// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useIntervalFn, useNow } from '@vueuse/core';
import { computed, shallowRef, toValue, watch, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { clockLength } from '@/lib/time';

type Anchor = { seconds: number; at: number };

// "01:02:03.456" as seconds; an empty or unreadable timecode reads as zero.
export function timecodeSeconds(timecode: string | null | undefined): number {
  const parts = (timecode ?? '').split(':').map(Number);

  if (parts.length === 0 || parts.some((part) => !Number.isFinite(part)))
    return 0;

  return parts.reduce((total: number, part) => total * 60 + part, 0);
}

// A running clock built from the last known timecode: it counts up every second by itself and starts over from
// each new timecode the server reports. `running` false gives empty text; `ticking` false (a pause) holds the value.
export function useLiveTimer(
  timecode: MaybeRefOrGetter<string | null | undefined>,
  running: MaybeRefOrGetter<boolean>,
  ticking: MaybeRefOrGetter<boolean> = true,
): ComputedRef<string> {
  const now = useNow({ scheduler: (tick: () => void) => useIntervalFn(tick, 1000) });
  const anchor = shallowRef<Anchor>({ seconds: 0, at: Date.now() });

  watch(
    [() => toValue(timecode), () => toValue(running)],
    () => {
      anchor.value = {
        seconds: timecodeSeconds(toValue(timecode)),
        at: Date.now(),
      };
    },
    { immediate: true, flush: 'sync' },
  );

  // While held (a pause), the clock stops at the moment the pause began; a resume counts on from that value.
  const heldAt = shallowRef<number | null>(null);

  watch(
    () => toValue(ticking),
    (active) => {
      if (!active) {
        heldAt.value = Date.now();

        return;
      }

      if (heldAt.value !== null) {
        anchor.value = {
          ...anchor.value,
          at: anchor.value.at + (Date.now() - heldAt.value),
        };
      }

      heldAt.value = null;
    },
    { immediate: true, flush: 'sync' },
  );

  const seconds = computed<number>(() => {
    const until = heldAt.value ?? now.value.getTime();

    return Math.floor(anchor.value.seconds + Math.max(0, until - anchor.value.at) / 1000);
  });

  return computed<string>(() =>
    toValue(running)
      ? clockLength(seconds.value)
      : '');
}
