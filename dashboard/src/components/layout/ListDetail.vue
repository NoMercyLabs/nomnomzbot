<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The list and detail layout: a list column, the detail beside it, and an optional aside.
// On a phone only one shows: the list, or the detail over the list once showDetail is true. There the layout
// itself is the one scroller, so a detail taller than the screen scrolls with the page.
// The aside sits in the row where it fits; narrower, it is a drawer from the right that asideOpen opens.
// Beside each other, the last panel in a column grows to the column's full height, so no column ends half way. A panel one wrapper deep (a search above a list) grows the same way.
import SidePanel from '@/components/layout/SidePanel.vue';

withDefaults(defineProps<{ showDetail?: boolean; asideLabel?: string }>(), {
  showDetail: false,
  asideLabel: '',
});

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
      class="flex min-h-0 min-w-0 flex-col md:*:last:flex-1 md:[&>:last-child>[data-slot=panel-card]:last-child]:flex-1"
      :class="showDetail ? '' : 'max-md:hidden'"
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
