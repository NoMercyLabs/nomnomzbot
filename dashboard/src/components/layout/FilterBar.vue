<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One filter for the page header bar. A short choice (2 to 4 options) is a segmented control;
// a longer one is a select with its label inside. The default slot holds a SearchField beside it.
import { computed, type ComputedRef } from 'vue'

import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

export interface FilterOption {
  value: string
  label: string
}

const props = defineProps<{ label: string; options: readonly FilterOption[] }>()

const selected = defineModel<string>({ required: true })

const segmented: ComputedRef<boolean> = computed<boolean>(() => props.options.length <= 4)

// A segmented control lets a person press the open segment again to clear it; a filter always keeps one.
function choose(value: unknown): void {
  if (typeof value === 'string' && value !== '') selected.value = value
}
</script>

<template>
  <div data-slot="filter-bar" class="flex flex-wrap items-center gap-2">
    <slot />
    <!-- The mockup segment (frame.css:158-160): a muted track of radius 8 with 2 px padding,
         so each item is 8 - 2 = 6; the open item is raised on the background. -->
    <ToggleGroup
      v-if="segmented"
      type="single"
      :spacing="1"
      class="gap-0.5 rounded-[8px] bg-muted p-0.5"
      :aria-label="label"
      :model-value="selected"
      @update:model-value="choose"
    >
      <ToggleGroupItem
        v-for="option in options"
        :key="option.value"
        :value="option.value"
        class="h-(--seg-h) min-w-0 rounded-[6px] px-2 text-muted-foreground data-[state=on]:bg-background data-[state=on]:text-foreground data-[state=on]:shadow-sm"
      >
        {{ option.label }}
      </ToggleGroupItem>
    </ToggleGroup>
    <Select v-else :model-value="selected" @update:model-value="choose">
      <SelectTrigger :aria-label="label">
        <span class="text-muted-foreground">{{ label }}</span>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        <SelectItem v-for="option in options" :key="option.value" :value="option.value">
          {{ option.label }}
        </SelectItem>
      </SelectContent>
    </Select>
  </div>
</template>
