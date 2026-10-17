<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's event types: a search box and one row per event. The open event is the `selected` type.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useEventResponses, type EventResponse } from '@/api/eventResponses';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { iconOf, nameOf } from './eventResponseDraft';

defineProps<{ selected: string | null }>();
const emit = defineEmits<{ pick: [eventType: string] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const responses = useEventResponses(channelId);

const search = ref('');

const state = usePanelState(responses, (data: EventResponse[]): boolean =>
  data.length === 0);

const rows = computed<EventResponse[]>(() => {
  const needle = search.value.trim().toLowerCase();

  return (responses.data.value ?? []).filter((row: EventResponse): boolean =>
    nameOf(row.eventType).toLowerCase().includes(needle),
  );
});

function detailOf(row: EventResponse): string {
  return row.followsPlatformDefault
    ? t('dashboard.eventResponses.list.default')
    : t('dashboard.eventResponses.list.own');
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col gap-3">
    <PanelCard
      :title="$t('dashboard.eventResponses.list.title')"
      :state="state"
      :empty-text="$t('dashboard.eventResponses.list.empty')"
      :error-text="$t('dashboard.eventResponses.list.error')"
      @retry="responses.refetch()"
    >
      <template #actions>
        <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
        <SearchField
          v-model="search"
          class="-mr-2 w-52 rounded-[12px]"
          :placeholder="$t('dashboard.eventResponses.list.search')"
        />
      </template>
      <p v-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t('dashboard.eventResponses.list.noMatch') }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.eventType"
        pick
        :selected="row.eventType === selected"
        :title="nameOf(row.eventType)"
        :detail="detailOf(row)"
        data-event-row
        :data-event-type="row.eventType"
        @click="emit('pick', row.eventType)"
      >
        <template #leading>
          <component :is="iconOf(row.eventType)" class="size-5" aria-hidden="true" />
        </template>
        <template v-if="!row.isEnabled" #end>
          <StatusBadge tone="off" :label="$t('dashboard.eventResponses.list.off')" />
        </template>
      </SummaryRow>
    </PanelCard>
  </div>
</template>
