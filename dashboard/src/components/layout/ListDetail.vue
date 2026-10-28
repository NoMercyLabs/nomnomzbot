<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The list and detail layout: a list column, the detail beside it, and an optional aside.
// On a phone only one shows: the list, or the detail over the list once showDetail is true. There the layout
// itself is the one scroller, so a detail taller than the screen scrolls with the page.
// The aside sits in the row where it fits; narrower, it is a drawer from the right that asideOpen opens.
// Beside each other, the last panel in a column grows to the column's full height, so no column ends half way. A panel one wrapper deep (a search above a list) grows the same way.
import SidePanel from '@/components/layout/SidePanel.vue';

// detailIdle: nothing is open yet, so the detail column holds only a hint. It keeps its own height instead of
// stretching to the column's full height.
withDefaults(
  defineProps<{
    showDetail?: boolean;
    asideLabel?: string;
    detailIdle?: boolean;
  }>(),
  {
    showDetail: false,
    asideLabel: '',
    detailIdle: false,
  },
);

const asideOpen = defineModel<boolean>('asideOpen', { default: false });
</script>

<template>
  <section
    data-slot="list-detail"
    data-page-body
    class="grid min-h-0 flex-1 gap-4 px-6 pt-4 pb-6 max-md:overflow-y-auto md:grid-cols-[minmax(16rem,22rem)_minmax(0,1fr)]"
    :class="
      $slots.aside ? 'xl:grid-cols-[minmax(16rem,22rem)_minmax(0,1fr)_minmax(14rem,18rem)]' : ''
    "
  >
    <div
      data-slot="list-detail-list"
      class="flex min-h-0 min-w-0 flex-col max-md:*:shrink-0 md:*:last:flex-1 md:[&>:last-child>[data-slot=panel-card]:last-child]:flex-1"
      :class="showDetail ? 'max-md:hidden' : ''"
    >
      <slot name="list" />
    </div>
    <div
      data-slot="list-detail-detail"
      class="flex min-h-0 min-w-0 flex-col"
      :class="[
        showDetail ? '' : 'max-md:hidden',
        detailIdle
          ? 'md:*:last:flex-none'
          : 'md:*:last:flex-1 md:[&>:last-child>[data-slot=panel-card]:last-child]:flex-1',
      ]"
    >
      <slot name="detail" />
    </div>
    <SidePanel
      v-if="$slots.aside"
      v-model:open="asideOpen"
      pinned
      data-slot="list-detail-aside"
      :label="asideLabel"
    >
      <slot name="aside" />
    </SidePanel>
  </section>
</template>
