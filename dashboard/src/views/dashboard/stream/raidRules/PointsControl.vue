<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How much a raid rule matters: a signed number with minus and plus buttons and a short word for its strength
// (strong plus, small minus). The exact value stays editable. The default slot takes the lines the server
// refused for this value, so they sit right under it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import Plus from '~icons/nnz/add';
import Minus from '~icons/nnz/remove';

const props = withDefaults(
  defineProps<{
    id?: string;
    label: string;
    canEdit: boolean;
  }>(),
  { id: undefined },
);

// The server refuses points beyond this; the buttons stop here so a click never builds a refused value.
const LIMIT = 100;
const STEP = 5;
const MEDIUM_FROM = 25;
const STRONG_FROM = 60;

const { t } = useI18n();
// The API types a weight as number or string; the control reads it as a number and writes a number.
const model = defineModel<number | string>({ required: true });
const points = computed(() => Number(model.value));

function levelOf(size: number): 'Strong' | 'Medium' | 'Small' {
  if (size >= STRONG_FROM)
    return 'Strong';

  if (size >= MEDIUM_FROM)
    return 'Medium';

  return 'Small';
}

const strength = computed(() => {
  if (points.value === 0)
    return 'none';

  const sign = points.value > 0
    ? 'plus'
    : 'minus';

  return `${sign}${levelOf(Math.abs(points.value))}`;
});

function change(typed: string | number): void {
  const next = Number(typed);

  if (typed === '' || !Number.isFinite(next)) return;

  model.value = Math.trunc(next);
}

function nudge(direction: 1 | -1): void {
  model.value = Math.min(LIMIT, Math.max(-LIMIT, points.value + direction * STEP));
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <Button
      v-if="props.canEdit"
      type="button"
      variant="outline"
      size="icon"
      data-points-down
      :aria-label="t('dashboard.stream.raids.lower')"
      :disabled="points <= -LIMIT"
      @click="nudge(-1)"
    >
      <Minus class="size-5" />
    </Button>
    <Input
      :id="props.id"
      type="number"
      data-points
      class="w-20 text-center"
      :model-value="points"
      :min="-LIMIT"
      :max="LIMIT"
      :aria-label="props.label"
      @update:model-value="change"
    />
    <Button
      v-if="props.canEdit"
      type="button"
      variant="outline"
      size="icon"
      data-points-up
      :aria-label="t('dashboard.stream.raids.raise')"
      :disabled="points >= LIMIT"
      @click="nudge(1)"
    >
      <Plus class="size-5" />
    </Button>
    <span data-strength class="min-w-24 text-sm text-muted-foreground">
      {{ t(`dashboard.stream.raids.strength.${strength}`) }}
    </span>
    <div class="basis-full empty:hidden">
      <slot />
    </div>
  </div>
</template>
