<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A ranked list (mockup .bl): each item is a label, a muted detail, its number, and a bar under them.
// The bars compare the items with the largest one; an item at 0 keeps a sliver so its row still reads.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

export type BarItem = {
  key: string;
  label: string;
  detail?: string;
  value: number;
};

const props = defineProps<{ items: readonly BarItem[] }>();

const { n } = useI18n();

const most = computed(() =>
  Math.max(0, ...props.items.map((item: BarItem) =>
    item.value)));

function width(value: number): string {
  const part = most.value === 0
    ? 0
    : value / most.value;

  return `${Math.max(1.5, part * 100)}%`;
}
</script>

<template>
  <ul data-slot="bar-list" class="grid gap-3">
    <li v-for="item in items" :key="item.key" data-bar-item class="grid gap-1.5">
      <div class="flex items-baseline gap-2 text-sm">
        <span class="min-w-0 font-medium">{{ item.label }}</span>
        <span v-if="item.detail" class="min-w-0 flex-1 text-muted-foreground">{{ item.detail }}</span>
        <span v-else class="flex-1" />
        <span class="font-medium tabular-nums">{{ n(item.value) }}</span>
      </div>
      <div class="h-1.5 overflow-hidden rounded-full bg-muted" aria-hidden="true">
        <div class="h-full rounded-full bg-foreground/60" :style="{ width: width(item.value) }" />
      </div>
    </li>
  </ul>
</template>
