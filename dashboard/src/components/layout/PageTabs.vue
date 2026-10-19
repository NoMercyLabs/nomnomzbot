<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts" generic="View extends string">
// The one tab row of a page (C2). The open tab is the route query ?view= (usePageView);
// the page reads the same composable to choose what to show under the header.
import { computed, useTemplateRef } from 'vue';
import Locked from '~icons/nnz/locked';

import type { PageTab } from '@/components/layout/pageTab';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { usePageView } from '@/composables/usePageView';
import { useScrollFade } from '@/composables/useScrollFade';

const props = defineProps<{ tabs: readonly PageTab<View>[] }>();

const values = computed(() => props.tabs.map((tab) => tab.value));
const view = usePageView(values);
const strip = useTemplateRef('strip');
const fade = useScrollFade(strip);
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
