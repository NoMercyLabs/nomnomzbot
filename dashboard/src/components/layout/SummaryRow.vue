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
import { computed } from 'vue';
import ChevronRight from '~icons/nnz/chevron-right';

defineOptions({ inheritAttrs: false });

const props = withDefaults(
  defineProps<{
    title: string;
    detail?: string;
    // A third, smaller line for who or when, under the detail.
    meta?: string;
    pick?: boolean;
    // A row that opens something else ends with a chevron.
    chevron?: boolean;
    selected?: boolean;
    // A row in a crowded panel: the text keeps a minimum width and cuts with an ellipsis instead of wrapping
    // one letter per line when the buttons beside it take the room.
    truncate?: boolean;
    // The trailing buttons drop to a second line, at the end, before the body is squeezed below 20rem (the title
    // would cut): a row in a panel whose width varies with the board's columns.
    wrap?: boolean;
  }>(),
  {
    detail: undefined,
    meta: undefined,
    pick: false,
    chevron: false,
    selected: false,
    truncate: false,
    wrap: false,
  },
);

const rowClass = computed<string>(() =>
  props.wrap
    ? 'flex-wrap'
    : '');
const sizeClass = computed<string>(() =>
  props.wrap
    ? 'flex-[1_1_20rem]'
    : 'flex-1');
const trailingClass = computed<string>(() =>
  props.wrap
    ? 'ml-auto'
    : '');

const textClass = computed<string>(() =>
  props.truncate
    ? 'min-w-24'
    : '');
const titleClass = computed<string>(() =>
  props.truncate
    ? 'truncate'
    : 'wrap-break-word');

const bodyClass = computed<string>(() => {
  if (props.selected)
    return 'bg-accent text-accent-foreground';

  return props.pick
    ? 'cursor-pointer hover:bg-foreground/5'
    : '';
});
</script>

<template>
  <div class="flex items-center gap-3" :class="rowClass">
    <component
      :is="pick ? 'button' : 'div'"
      :type="pick ? 'button' : undefined"
      data-slot="summary-row-body"
      class="flex min-h-11 min-w-0 items-center gap-3 rounded-[8px] px-3 py-2 text-left [&>[data-variant=ghost]:first-child]:-ml-2.5 [&>[data-variant=ghost]:last-child]:-mr-2.5 [&>[data-variant=ghost][data-size^=icon]:first-child]:-ml-2 [&>[data-variant=ghost][data-size^=icon]:last-child]:-mr-2"
      :class="[sizeClass, bodyClass]"
      v-bind="$attrs"
    >
      <slot name="leading" />
      <span data-slot="summary-row-text" class="min-w-0 flex-1" :class="textClass">
        <!-- Wrap, never cut: on a phone a cut detail loses its meaning, and a long bot address its end.
             A row that sets truncate cuts instead, and keeps a minimum width. -->
        <span data-slot="summary-row-title"
              class="block text-sm font-medium"
              :class="titleClass"
        ><slot name="title">{{ title }}</slot></span>
        <span v-if="detail" class="flex items-center gap-2 text-sm text-muted-foreground">
          <slot name="detail-lead" />
          <span :class="truncate ? 'truncate' : ''">{{ detail }}</span>
        </span>
        <span
          v-if="meta || $slots.meta"
          data-slot="summary-row-meta"
          class="block text-xs text-muted-foreground"
        ><slot name="meta">{{ meta }}</slot></span>
      </span>
      <slot name="end" />
      <ChevronRight
        v-if="chevron"
        data-slot="summary-row-chevron"
        class="size-4 flex-none text-muted-foreground"
        aria-hidden="true"
      />
    </component>
    <div
      v-if="$slots.trailing"
      data-slot="summary-row-trailing"
      :class="trailingClass"
      class="flex shrink-0 items-center gap-2 pr-3 [&>[data-variant=ghost]:last-child]:-mr-2.5 [&>[data-variant=ghost][data-size^=icon]:last-child]:-mr-2"
    >
      <slot name="trailing" />
    </div>
  </div>
</template>
