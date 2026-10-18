<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
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

const commands = useSupportTenantItems('commands', channelId, () =>
  props.justification);
const pipelines = useSupportTenantItems('pipelines', channelId, () =>
  props.justification);
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
    <ul class="grid divide-y divide-border text-sm">
      <li
        v-for="row in historyRows"
        :key="row.eventId"
        data-history-row
        class="flex items-center gap-3 px-2 py-2"
      >
        <span class="min-w-0 flex-1 font-medium">{{ row.eventType }}</span>
        <span class="text-muted-foreground">{{
          row.channelName ?? t("dashboard.admin.support.history.platform")
        }}</span>
        <span class="text-muted-foreground tabular-nums">
          <DateTime :value="row.occurredAt" variant="date" />
        </span>
      </li>
    </ul>
  </PanelCard>
  <PanelCard
    :title="t('dashboard.admin.support.items.title')"
    :state="itemState"
    :empty-text="t('dashboard.admin.support.items.empty')"
    :error-text="t('dashboard.admin.support.items.error')"
    @retry="itemQuery.refetch()"
  >
    <template #actions>
      <select
        v-if="connections.length > 1"
        v-model="picked"
        class="h-8 rounded-md border bg-background px-2 text-sm"
        :aria-label="t('dashboard.admin.support.items.channel')"
      >
        <option v-for="row in connections" :key="row.broadcasterId" :value="row.broadcasterId">
          {{ row.channelName }}
        </option>
      </select>
      <SegmentedControl
        v-model="kind"
        :label="t('dashboard.admin.support.items.title')"
        :options="kinds"
      />
    </template>
    <ul class="grid divide-y divide-border text-sm">
      <li
        v-for="row in items"
        :key="row.id"
        :data-command-row="kind === 'commands' ? '' : undefined"
        :data-pipeline-row="kind === 'pipelines' ? '' : undefined"
        class="flex items-center gap-3 px-2 py-2"
      >
        <span class="min-w-0 flex-1 font-medium">{{ row.name }}</span>
        <span class="text-muted-foreground">
          {{
            row.isEnabled
              ? t("dashboard.admin.support.items.on")
              : t("dashboard.admin.support.items.off")
          }}
        </span>
      </li>
    </ul>
  </PanelCard>
</template>
