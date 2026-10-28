<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts" generic="View extends string">
// The one tab row of a page (C2). The open tab is the route query ?view= (usePageView);
// the page reads the same composable to choose what to show under the header.
import { computed, nextTick, useTemplateRef, watch } from 'vue';
import Locked from '~icons/nnz/locked';

import type { PageTab } from '@/components/layout/pageTab';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { usePageView } from '@/composables/usePageView';
import { useScrollFade } from '@/composables/useScrollFade';

const props = defineProps<{ tabs: readonly PageTab<View>[] }>();

const values = computed(() => props.tabs.map((tab) => tab.value));
const view = usePageView(values);
const strip = useTemplateRef('strip');
const fade = useScrollFade(() => strip.value?.$el);

// A long strip scrolls sideways; the open tab is brought into view, so a link to the last tab never hides it.
watch(
  view,
  async () => {
    await nextTick();
    const element = strip.value?.$el as HTMLElement | undefined;

    const tab = element?.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]');

    // Only the strip moves. scrollIntoView would also scroll the page to a strip that is off screen, such as
    // a preview on the home page that mounts before it is seen.
    if (element && tab) {
      element.scrollLeft = tab.offsetLeft - (element.clientWidth - tab.offsetWidth) / 2;
    }
  },
  { immediate: true, flush: 'post' },
);
</script>

<template>
  <!-- A phone swipes a long tab row sideways; it never widens the page. The edge that hides tabs fades. -->
  <Tabs
    ref="strip"
    v-model="view"
    class="min-w-0 overflow-x-auto overflow-y-hidden scrollbar-none"
    :class="fade"
  >
    <TabsList>
      <!-- A locked tab ignores the pointer, so its reason is the tooltip of the wrapper around it. -->
      <span v-for="tab in tabs" :key="tab.value" :title="tab.reason" class="flex flex-1">
        <TabsTrigger :value="tab.value" :disabled="tab.disabled">
          <Locked v-if="tab.disabled" data-slot="tab-lock" aria-hidden="true" />
          {{ tab.label }}
          <span v-if="tab.count !== undefined" class="text-xs text-muted-foreground tabular-nums">{{
            tab.count
          }}</span>
        </TabsTrigger>
      </span>
    </TabsList>
  </Tabs>
</template>
