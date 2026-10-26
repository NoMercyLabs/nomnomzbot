<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The panel of a list page: a PanelCard whose head carries the list's search, in the same spot on every page
// (owner 2026-10-26: "the search baked into the list header like pipeline and overlay"). Extra head actions
// (a sort, a filter) go in the actions slot and sit left of the search, so the search keeps the right edge.
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import type { PanelState } from '@/composables/usePanelState';

withDefaults(
  defineProps<{
    title: string;
    placeholder: string;
    description?: string;
    state?: PanelState;
    emptyText?: string;
    errorText?: string;
    // A phone list with no head: the page then puts its own search above the card.
    headless?: boolean;
  }>(),
  {
    description: undefined,
    state: 'ready',
    emptyText: undefined,
    errorText: undefined,
    headless: false,
  },
);

defineEmits<{ retry: [] }>();

const search = defineModel<string>({ default: '' });
</script>

<template>
  <PanelCard
    :title="title"
    :description="description"
    :state="state"
    :empty-text="emptyText"
    :error-text="errorText"
    :headless="headless"
    @retry="$emit('retry')"
  >
    <template #actions>
      <slot name="actions" />
      <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
      <SearchField
        v-model="search"
        class="-mr-2 w-52 rounded-[12px]"
        :placeholder="placeholder"
      />
    </template>
    <slot />
    <template v-if="$slots.footer" #footer>
      <slot name="footer" />
    </template>
  </PanelCard>
</template>
