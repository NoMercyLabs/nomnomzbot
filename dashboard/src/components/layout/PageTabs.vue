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
import { computed } from 'vue';

import type { PageTab } from '@/components/layout/pageTab';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { usePageView } from '@/composables/usePageView';

const props = defineProps<{ tabs: readonly PageTab<View>[] }>();

const values = computed(() => props.tabs.map((tab) => tab.value));
const view = usePageView(values);
</script>

<template>
  <!-- A phone swipes a long tab row sideways; it never widens the page. -->
  <Tabs v-model="view" class="min-w-0 overflow-x-auto scrollbar-none">
    <TabsList>
      <TabsTrigger v-for="tab in tabs" :key="tab.value" :value="tab.value">
        {{ tab.label }}
        <span v-if="tab.count !== undefined" class="text-xs text-muted-foreground tabular-nums">{{
          tab.count
        }}</span>
      </TabsTrigger>
    </TabsList>
  </Tabs>
</template>
