<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The areas of the bot's actions (moderation, economy, music, ...). A row opens the actions of that area;
// the search finds an action by its key or description in every area at once.
import { useActionMatrix } from '@/api/permissions';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import type { ActionArea } from '../actionArea';

// areas: the areas left after the search, grouped by the page, which also opens the picked one.
defineProps<{ areas: readonly ActionArea[]; selected: string | undefined }>();
const search = defineModel<string>('search', { default: '' });
const emit = defineEmits<{ pick: [area: string] }>();

const { channelId } = useCurrentChannel();
const matrix = useActionMatrix(channelId);
const state = usePanelState(matrix, (rows) =>
  rows.length === 0);
</script>

<template>
  <PanelCard
    data-panel="action-areas"
    :title="$t('dashboard.settings.permissions.areas.title')"
    :state="state"
    :empty-text="$t('dashboard.settings.permissions.matrix.empty')"
    :error-text="$t('dashboard.settings.permissions.matrix.loadFailed')"
    @retry="matrix.refetch()"
  >
    <template #actions>
      <!-- Concentric with the panel corner, as in the command list: a 12px radius. -->
      <SearchField
        v-model="search"
        data-area-search
        class="-mr-2 w-52 rounded-[12px]"
        :placeholder="$t('dashboard.settings.permissions.areas.search')"
      />
    </template>
    <p v-if="areas.length === 0" class="p-4 text-center text-sm text-muted-foreground">
      {{ $t('dashboard.settings.permissions.areas.noMatch') }}
    </p>
    <SummaryRow
      v-for="group in areas"
      v-else
      :key="group.area"
      pick
      chevron
      :data-area="group.area"
      :selected="group.area === selected"
      :title="group.label"
      :detail="$t('dashboard.settings.permissions.areas.count', group.rows.length)"
      :meta="group.changed > 0 ? $t('dashboard.settings.permissions.areas.changed', group.changed) : undefined"
      @click="emit('pick', group.area)"
    />
  </PanelCard>
</template>
