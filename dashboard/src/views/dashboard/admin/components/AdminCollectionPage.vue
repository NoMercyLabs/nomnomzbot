<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A paged admin list: header with the result count (and a filter bar in the header's bar slot), a dense body,
// filters and a notice above one lead panel with an optional search, and the pager. The pager sits in the panel
// footer from the tablet width up and in the sticky page footer on a phone. A panel slot opens a detail beside the
// list. The page keeps its data, its panel state and its table.
import ListPanel from '@/components/layout/ListPanel.vue';
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import { useScreenFrom } from '@/composables/useLayout';
import type { PanelState } from '@/composables/usePanelState';

import AdminPager from './AdminPager.vue';

defineProps<{
  title: string;
  // The ready-made result count; absent until the first answer arrives.
  count?: string;
  page: number;
  pages: number;
  state: PanelState;
  listTitle: string;
  description?: string;
  emptyText?: string;
  errorText?: string;
  // Set: the lead panel carries a search field, bound to v-model:search.
  searchPlaceholder?: string;
}>();

const search = defineModel<string>('search', { default: '' });

defineEmits<{
  previous: [];
  next: [];
  retry: [];
}>();

const tablet = useScreenFrom('tablet');
</script>

<template>
  <PageHeader :title="title" :crumb="$t('dashboard.admin.crumb')">
    <template #status>
      <span v-if="count" class="text-sm text-muted-foreground tabular-nums">{{ count }}</span>
    </template>
    <template v-if="$slots.bar" #bar>
      <slot name="bar" />
    </template>
  </PageHeader>
  <PanelBeside>
    <PageBody dense data-page-body>
      <slot name="filters" />
      <slot name="notice" />
      <ListPanel
        v-model="search"
        tier="lead"
        class="min-h-80 min-w-0 flex-1"
        :title="listTitle"
        :placeholder="searchPlaceholder"
        :description="description"
        :state="state"
        :empty-text="emptyText"
        :error-text="errorText"
        @retry="$emit('retry')"
      >
        <slot />
        <template v-if="tablet" #footer>
          <AdminPager :page="page" :pages="pages" @previous="$emit('previous')" @next="$emit('next')" />
        </template>
      </ListPanel>
      <slot name="secondary" />
      <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
      <PageActionFooter v-if="!tablet">
        <AdminPager :page="page" :pages="pages" @previous="$emit('previous')" @next="$emit('next')" />
      </PageActionFooter>
    </PageBody>
    <template #panel>
      <slot name="panel" />
    </template>
  </PanelBeside>
</template>
