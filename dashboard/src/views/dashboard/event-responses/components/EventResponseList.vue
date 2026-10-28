<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's event types: a search box and one row per event. The open event is the `selected` type.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useEventResponses, type EventResponse } from '@/api/eventResponses';
import ListPanel from '@/components/layout/ListPanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { iconOf } from './eventResponseDraft';
import { useEventName } from './useEventName';

defineProps<{ selected: string | null }>();
const emit = defineEmits<{ pick: [eventType: string] }>();

const { t } = useI18n();
const eventName = useEventName();
const { channelId } = useCurrentChannel();
const responses = useEventResponses(channelId);

const search = ref('');

const state = usePanelState(responses, (data: EventResponse[]): boolean =>
  data.length === 0);

const rows = computed<EventResponse[]>(() => {
  const needle = search.value.trim().toLowerCase();

  return (responses.data.value ?? []).filter((row): boolean =>
    eventName(row.eventType).toLowerCase().includes(needle),
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
    <ListPanel
      v-model="search"
      :title="$t('dashboard.eventResponses.list.title')"
      :placeholder="$t('dashboard.eventResponses.list.search')"
      :state="state"
      :empty-text="$t('dashboard.eventResponses.list.empty')"
      :error-text="$t('dashboard.eventResponses.list.error')"
      @retry="responses.refetch()"
    >
      <p v-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t('dashboard.eventResponses.list.noMatch') }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.eventType"
        pick
        :selected="row.eventType === selected"
        :title="eventName(row.eventType)"
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
    </ListPanel>
  </div>
</template>
