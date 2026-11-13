<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A paged admin list: header with the result count, a dense body, filters and a notice above one lead panel, and
// the pager. The pager sits in the panel footer from the tablet width up and in the sticky page footer on a phone.
// The page keeps its data, its panel state and its table.
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
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
}>();

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
  </PageHeader>
  <PageBody dense data-page-body>
    <slot name="filters" />
    <slot name="notice" />
    <PanelCard
      tier="lead"
      class="min-h-80 min-w-0 flex-1"
      :title="listTitle"
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
    </PanelCard>
    <slot name="secondary" />
    <!-- On a phone the list takes its full height and the pager rides in the sticky page footer. -->
    <PageActionFooter v-if="!tablet">
      <AdminPager :page="page" :pages="pages" @previous="$emit('previous')" @next="$emit('next')" />
    </PageActionFooter>
  </PageBody>
</template>
