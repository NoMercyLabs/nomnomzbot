<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one tab row of a page (C2). The open tab is the route query ?view= (usePageView);
// the page reads the same composable to choose what to show under the header.
import { computed } from 'vue'

import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { usePageView } from '@/composables/usePageView'

export interface PageTab {
  value: string
  label: string
  count?: number
}

const props = defineProps<{ tabs: readonly PageTab[] }>()

const values = computed<string[]>(() => props.tabs.map((tab: PageTab) => tab.value))
const view = usePageView(values)
</script>

<template>
  <Tabs v-model="view">
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
