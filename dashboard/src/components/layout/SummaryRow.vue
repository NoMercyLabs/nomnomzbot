<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One item of a RowGroup (V13, V21): a 14 medium title over a 14 muted detail line.
// pick: the whole row is the control (a button; pass role="radio" for a single choice).
// Rows that carry their own button stay plain and do not hover.
// Balance: the first ink sits 12px (px-3) from the row edge on both sides. A trailing button sits
// outside the body, so its wrapper adds pr-3. A ghost button has no border, so its ink starts
// one padding inside it: it gets a negative margin equal to that padding (10px, 8px for icons).
defineOptions({ inheritAttrs: false });

withDefaults(
  defineProps<{
    title: string;
    detail?: string;
    pick?: boolean;
    selected?: boolean;
  }>(),
  {
    detail: undefined,
    pick: false,
    selected: false,
  },
);
</script>

<template>
  <div class="flex items-center gap-3">
    <component
      :is="pick ? 'button' : 'div'"
      :type="pick ? 'button' : undefined"
      data-slot="summary-row-body"
      class="flex min-h-11 min-w-0 flex-1 items-center gap-3 rounded-[8px] px-3 py-2 text-left [&>[data-variant=ghost]:first-child]:-ml-2.5 [&>[data-variant=ghost]:last-child]:-mr-2.5 [&>[data-variant=ghost][data-size^=icon]:first-child]:-ml-2 [&>[data-variant=ghost][data-size^=icon]:last-child]:-mr-2"
      :class="[
        selected
          ? 'bg-accent text-accent-foreground'
          : pick
            ? 'cursor-pointer hover:bg-foreground/5'
            : '',
      ]"
      v-bind="$attrs"
    >
      <slot name="leading" />
      <span class="min-w-0 flex-1">
        <!-- Wrap, never cut: on a phone a cut detail loses its meaning, and a long bot address its end. -->
        <span class="block text-sm font-medium wrap-break-word"><slot name="title">{{ title }}</slot></span>
        <span v-if="detail" class="block text-sm text-muted-foreground">{{ detail }}</span>
      </span>
      <slot name="end" />
    </component>
    <div
      v-if="$slots.trailing"
      data-slot="summary-row-trailing"
      class="flex shrink-0 items-center gap-2 pr-3 [&>[data-variant=ghost]:last-child]:-mr-2.5 [&>[data-variant=ghost][data-size^=icon]:last-child]:-mr-2"
    >
      <slot name="trailing" />
    </div>
  </div>
</template>
