<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// An event bit mask as a list of named checkboxes. The server sends the kinds (name, bit, on by default), so
// the list and its defaults are never written in the dashboard. The model is the mask number: ticking a kind
// adds its bit, unticking removes only that bit, and the reset button sets the mask to the default kinds.
// The label of a kind is the resource key `${prefix}.${name}`.
import { computed } from 'vue';

import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';

type EventKindItem = {
  name: string;
  bit: number | string;
  onByDefault: boolean;
};

const props = defineProps<{
  kinds: readonly EventKindItem[];
  prefix: string;
  disabled?: boolean;
}>();

const mask = defineModel<number>({ required: true });

const defaultMask = computed<number>(() =>
  props.kinds.reduce(
    (sum, kind) =>
      kind.onByDefault
        ? sum | Number(kind.bit)
        : sum,
    0,
  ),
);

function isOn(kind: EventKindItem): boolean {
  return (mask.value & Number(kind.bit)) !== 0;
}

function toggle(kind: EventKindItem, on: boolean | 'indeterminate'): void {
  if (props.disabled) return;

  const bit = Number(kind.bit);

  mask.value = on === true
    ? mask.value | bit
    : mask.value & ~bit;
}

function reset(): void {
  mask.value = defaultMask.value;
}
</script>

<template>
  <div data-event-kind-mask class="grid gap-2">
    <ul class="grid gap-2 sm:grid-cols-2">
      <li v-for="kind in kinds" :key="kind.name">
        <label class="flex items-center gap-2 text-sm">
          <Checkbox
            :data-kind="kind.name"
            :model-value="isOn(kind)"
            :disabled="disabled"
            @update:model-value="toggle(kind, $event)"
          />
          {{ $t(`${prefix}.${kind.name}`) }}
        </label>
      </li>
    </ul>
    <div v-if="!disabled" class="flex">
      <Button data-kind-reset
              type="button"
              variant="ghost"
              size="sm"
              @click="reset"
      >
        {{ $t('common.resetToDefaults') }}
      </Button>
    </div>
  </div>
</template>
