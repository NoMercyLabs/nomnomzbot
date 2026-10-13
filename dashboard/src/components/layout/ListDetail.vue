<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The list and detail layout: a list column, the detail beside it, and an optional aside.
// On a phone only one shows: the list, or the detail over the list once showDetail is true.
withDefaults(defineProps<{ showDetail?: boolean }>(), { showDetail: false })
</script>

<template>
  <section
    data-slot="list-detail"
    class="grid min-h-0 flex-1 gap-4 px-6 pt-4 pb-6 md:grid-cols-[minmax(16rem,22rem)_minmax(0,1fr)]"
    :class="$slots.aside ? 'xl:grid-cols-[minmax(16rem,22rem)_minmax(0,1fr)_minmax(14rem,18rem)]' : ''"
  >
    <div data-slot="list-detail-list" class="flex min-h-0 min-w-0 flex-col" :class="showDetail ? 'max-md:hidden' : ''">
      <slot name="list" />
    </div>
    <div data-slot="list-detail-detail" class="flex min-h-0 min-w-0 flex-col" :class="showDetail ? '' : 'max-md:hidden'">
      <slot name="detail" />
    </div>
    <div v-if="$slots.aside" data-slot="list-detail-aside" class="flex min-h-0 min-w-0 flex-col max-xl:hidden">
      <slot name="aside" />
    </div>
  </section>
</template>
