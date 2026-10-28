<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Any number of named options chosen at once: each option is its own on/off press (the mockup's
// .btn.outline.sm.toggle) and none chosen is allowed. The row wraps when the options do not fit one line.
import type { AcceptableValue } from 'reka-ui';
import Check from '~icons/nnz/check';

import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

defineProps<{ options: readonly { value: string; label: string }[] }>();

const chosen = defineModel<string[]>({ required: true });

function choose(next: AcceptableValue | AcceptableValue[]): void {
  if (Array.isArray(next)) {
    chosen.value = next.map(String);
  }
}
</script>

<template>
  <!-- The mockup's toggle press: an outline small button, filled with --muted while pressed. -->
  <ToggleGroup
    type="multiple"
    :spacing="2"
    class="flex-wrap"
    :model-value="chosen"
    @update:model-value="choose"
  >
    <ToggleGroupItem
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      class="h-(--btn-h-sm) min-w-0 gap-1.5 rounded-sm border border-input px-3 font-medium text-muted-foreground hover:border-foreground/65 hover:bg-muted data-[state=on]:bg-muted data-[state=on]:text-foreground"
    >
      <!-- A fill alone is too faint to tell pressed from not; the check carries the state too. -->
      <Check v-if="chosen.includes(option.value)" aria-hidden="true" />
      {{ option.label }}
    </ToggleGroupItem>
  </ToggleGroup>
</template>
