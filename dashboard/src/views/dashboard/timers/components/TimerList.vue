<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's timers: a search box and one row per timer. The open timer is the `selected` id.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTimers, type Timer } from '@/api/liveOps';
import ListPanel from '@/components/layout/ListPanel.vue';
import type { PanelState } from '@/composables/usePanelState';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

defineProps<{ selected: string | null }>();
const emit = defineEmits<{ pick: [id: string]; create: [] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const timers = useTimers(channelId);

const search = ref('');

const panel = usePanelState(timers, (data: Timer[]): boolean =>
  data.length === 0);
// The empty state holds a button, so the panel shows it as ready content.
const state = computed<PanelState>(() =>
  panel.value === 'empty'
    ? 'ready'
    : panel.value);
const none = computed<boolean>(() => panel.value === 'empty');

const rows = computed<Timer[]>(() => {
  const needle = search.value.trim().toLowerCase();

  return (timers.data.value ?? []).filter((row): boolean =>
    row.name.toLowerCase().includes(needle),
  );
});

function detailOf(row: Timer): string {
  const minutes = Number(row.intervalMinutes);
  const count = Number(row.messageCount);
  const cadence = row.fireOnce
    ? t('dashboard.timers.list.once')
    : t('dashboard.timers.list.every', { minutes }, minutes);

  return `${cadence} · ${t('dashboard.timers.list.messages', { count }, count)}`;
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col gap-3">
    <ListPanel
      v-model="search"
      :title="$t('dashboard.timers.list.title')"
      :placeholder="$t('dashboard.timers.list.search')"
      :state="state"
      :error-text="$t('dashboard.timers.list.error')"
      @retry="timers.refetch()"
    >
      <div
        v-if="none"
        data-slot="panel-empty"
        class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
      >
        {{ $t('dashboard.timers.list.empty') }}
        <Button v-if="can('timers:write')" type="button" size="sm" @click="emit('create')">{{
          $t('dashboard.timers.list.makeFirst')
        }}</Button>
      </div>
      <p v-else-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t('dashboard.timers.list.noMatch') }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.id"
        pick
        :selected="row.id === selected"
        :title="row.name"
        :detail="detailOf(row)"
        data-timer-row
        :data-timer-name="row.name"
        @click="emit('pick', row.id)"
      >
        <template v-if="!row.isEnabled" #end>
          <StatusBadge tone="off" :label="$t('dashboard.timers.list.off')" />
        </template>
      </SummaryRow>
    </ListPanel>
  </div>
</template>
