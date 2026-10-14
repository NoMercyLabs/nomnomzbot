<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts" generic="T extends string">
// One choice from 2 to 4 named options. One option is always chosen: pressing the open one again,
// which would clear it in the library, changes nothing.
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';

defineProps<{ label: string; options: readonly { value: T; label: string }[] }>();

const chosen = defineModel<T>({ required: true });

function choose(next: unknown): void {
  if (typeof next === 'string' && next !== '') {
    chosen.value = next as T;
  }
}
</script>

<template>
  <!-- The mockup segment (frame.css:158-160): a muted track of radius 8 with 2 px padding,
       so each item is 8 - 2 = 6; the open item is raised on the background.
       Equal grid columns: every option is as wide as the widest label, none squeezed below its text. -->
  <ToggleGroup
    type="single"
    :spacing="1"
    class="grid auto-cols-fr grid-flow-col gap-0.5 rounded-[8px] bg-muted p-0.5"
    :aria-label="label"
    :model-value="chosen"
    @update:model-value="choose"
  >
    <ToggleGroupItem
      v-for="option in options"
      :key="option.value"
      :value="option.value"
      class="h-(--seg-h) rounded-[6px] px-2 text-muted-foreground data-[state=on]:bg-background data-[state=on]:text-foreground data-[state=on]:shadow-sm"
    >
      {{ option.label }}
    </ToggleGroupItem>
  </ToggleGroup>
</template>
