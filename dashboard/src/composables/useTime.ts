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

import { useIntervalFn } from '@vueuse/core';
import { computed, type ComputedRef, type MaybeRefOrGetter, shallowRef, toValue } from 'vue';
import { useI18n } from 'vue-i18n';

import { formatTime, TIME_VARIANTS, type TimeValue, type TimeVariant } from '@/lib/time';

export type TimeFormatters = Record<TimeVariant, (value: TimeValue) => string>;

export type FormattedTimeOptions = { live?: boolean };

const SECOND_MS = 1000;
const NEAR_MS = 60_000;
const FAR_TICK_MS = 30_000;

// One named formatter per variant (time.relative(at), time.dateTime(at), time.duration(seconds)), bound to the
// signed-in person's language. Each call reads the i18n locale, so a computed or a template re-renders on a switch.
export function useTime(): TimeFormatters {
  const { locale } = useI18n();
  const formatters: Partial<TimeFormatters> = {};

  for (const variant of TIME_VARIANTS) {
    formatters[variant] = (value: TimeValue): string => formatTime(value, variant, locale.value);
  }

  return formatters as TimeFormatters;
}

// Seconds apart while the moment is within a minute of now, then every half minute.
function tickEvery(value: TimeValue, now: Date): number {
  const apart = Math.abs(new Date(value ?? now).getTime() - now.getTime());

  return apart < NEAR_MS
    ? SECOND_MS
    : FAR_TICK_MS;
}

// A reactive text for a template or a computed. With `live`, a relative text moves on by itself as time passes.
export function useFormattedTime(
  value: MaybeRefOrGetter<TimeValue>,
  variant: TimeVariant,
  options: FormattedTimeOptions = {},
): ComputedRef<string> {
  const { locale } = useI18n();
  const live = options.live === true && variant === 'relative';
  const now = shallowRef<Date>(new Date());
  const interval = computed<number>(() => tickEvery(toValue(value), now.value));

  useIntervalFn(() => {
    now.value = new Date();
  }, interval, { immediate: live });

  return computed<string>(() => formatTime(toValue(value), variant, locale.value, now.value));
}
