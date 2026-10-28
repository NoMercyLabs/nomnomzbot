<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts" generic="T extends string">
// One choice from 2 to 4 named options. One option is always chosen: pressing the open one again,
// which would clear it in the library, changes nothing. `none` shows no option chosen, for a list
// whose options can match nothing (the readability presets once an option was changed after them).
import type { AcceptableValue } from 'reka-ui';

import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

defineProps<{
  label: string;
  options: readonly { value: T; label: string }[];
  disabled?: boolean;
  none?: boolean;
}>();

const chosen = defineModel<T>({ required: true });

function choose(next: AcceptableValue | AcceptableValue[]): void {
  if (typeof next === 'string' && next !== '') {
    chosen.value = next as T;
  }
}
</script>

<template>
  <!-- frame.css [data-segmented]: in the page control bar it wears the page tab track; inside a panel it is
       the mockup's compact segment, the options sharing the row. No option shrinks below its label. -->
  <ToggleGroup
    type="single"
    :spacing="1"
    data-segmented
    :aria-label="label"
    :model-value="none ? '' : chosen"
    :disabled="disabled"
    @update:model-value="choose"
  >
    <ToggleGroupItem
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      data-segment
      class="shrink-0 text-muted-foreground hover:bg-transparent data-[state=on]:text-foreground"
    >
      {{ option.label }}
    </ToggleGroupItem>
  </ToggleGroup>
</template>
