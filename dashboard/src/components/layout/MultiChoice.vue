<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Any number of named options chosen at once: each option is its own on/off press (the mockup's
// .btn.outline.sm.toggle) and none chosen is allowed. The row wraps when the options do not fit one line.
import Check from '~icons/nnz/check';

import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

defineProps<{ options: readonly { value: string; label: string }[] }>();

const chosen = defineModel<string[]>({ required: true });

function choose(next: unknown): void {
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
