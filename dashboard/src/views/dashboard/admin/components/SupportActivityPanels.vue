<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What happened to a person (the recorded events, newest first) and what their channel built for itself:
// its own commands and pipelines. Read only.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useSupportHistory,
  useSupportPerson,
  useSupportTenantItems,
  type SupportTenantKind,
} from '@/api/adminSupport';
import DateTime from '@/components/layout/DateTime.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{ personId: string; justification: string }>();

const { t } = useI18n();

const history = useSupportHistory(
  () => props.personId,
  () => props.justification,
);
const historyRows = computed(() => history.data.value?.data ?? []);
const historyState = usePanelState(history, (page) =>
  (page.data?.length ?? 0) === 0);

const person = useSupportPerson(
  () => props.personId,
  () => props.justification,
);
const connections = computed(() => person.data.value?.platformConnections ?? []);

const kind = ref<SupportTenantKind>('commands');
const picked = ref<string | null>(null);
const channelId = computed<string | null>(
  () => picked.value ?? connections.value[0]?.broadcasterId ?? null,
);

const channelChoices = computed(() =>
  connections.value.map((row) => ({ value: row.broadcasterId, label: row.channelName })));

const commands = useSupportTenantItems('commands', channelId, () => props.justification);
const pipelines = useSupportTenantItems('pipelines', channelId, () => props.justification);
const itemQuery = computed(() =>
  kind.value === 'commands'
    ? commands
    : pipelines);
const items = computed(() => itemQuery.value.data.value?.data ?? []);
const itemState = usePanelState(
  {
    data: computed(() => itemQuery.value.data.value),
    isPending: computed(() => itemQuery.value.isPending.value),
    isError: computed(() => itemQuery.value.isError.value),
  },
  (page) => (page.data?.length ?? 0) === 0,
);

const kinds = computed(() =>
  (['commands', 'pipelines'] as const).map((value) => ({
    value,
    label: t(`dashboard.admin.support.items.${value}`),
  })),
);
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.support.history.title')"
    :state="historyState"
    :empty-text="t('dashboard.admin.support.history.empty')"
    :error-text="t('dashboard.admin.support.history.error')"
    @retry="history.refetch()"
  >
    <RowGroup>
      <SummaryRow
        v-for="row in historyRows"
        :key="row.eventId"
        data-history-row
        :title="row.eventType"
        :detail="row.channelName ?? t('dashboard.admin.support.history.platform')"
      >
        <template #meta>
          <DateTime :value="row.occurredAt" variant="date" />
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
  <PanelCard
    :title="t('dashboard.admin.support.items.title')"
    :state="itemState"
    :empty-text="t('dashboard.admin.support.items.empty')"
    :error-text="t('dashboard.admin.support.items.error')"
    @retry="itemQuery.refetch()"
  >
    <template #actions>
      <OptionSelect
        v-if="connections.length > 1"
        :model-value="channelId ?? ''"
        class="w-48"
        :options="channelChoices"
        :aria-label="t('dashboard.admin.support.items.channel')"
        @update:model-value="picked = $event"
      />
      <SegmentedControl
        v-model="kind"
        :label="t('dashboard.admin.support.items.title')"
        :options="kinds"
      />
    </template>
    <RowGroup>
      <SummaryRow
        v-for="row in items"
        :key="row.id"
        :data-command-row="kind === 'commands' ? '' : undefined"
        :data-pipeline-row="kind === 'pipelines' ? '' : undefined"
        :title="row.name"
      >
        <template #end>
          <StatusBadge
            :tone="row.isEnabled ? 'ok' : 'off'"
            :label="row.isEnabled ? t('dashboard.admin.support.items.on') : t('dashboard.admin.support.items.off')"
          />
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>
