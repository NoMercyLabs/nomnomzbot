<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One filter for the page control bar. A short choice (2 to 4 options) is a segmented control;
// a longer one is a select with its label inside. The search stays above the list, never in the
// bar (owner 2026-10-14: "only th controls, keep the search above").
import type { AcceptableValue } from 'reka-ui';
import { computed } from 'vue';

import type { FilterOption } from '@/components/layout/filterOption';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

const props = defineProps<{ label: string; options: readonly FilterOption[] }>();

// No slots: a page that puts a search in here fails the type check.
defineSlots<Record<string, never>>();

const selected = defineModel<string>({ required: true });

const segmented = computed<boolean>(() => props.options.length <= 4);

// The select can send an empty value; a filter always keeps one.
function choose(value: AcceptableValue): void {
  if (typeof value === 'string' && value !== '') {
    selected.value = value;
  }
}
</script>

<template>
  <div data-slot="filter-bar" class="flex flex-wrap items-center gap-2">
    <SegmentedControl v-if="segmented" v-model="selected" :label="label" :options="options" />
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
