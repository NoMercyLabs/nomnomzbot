<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// Event replay: an operator re-applies the journal events of one channel and one time window to one
// projection. A preview counts the events first and replays nothing. The replay runs only on that
// counted number, after a confirm dialog names the channel, the projection and the count. The list of
// executed replays comes from the audit log and reloads when the admin hub reports a finished replay.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminTenants } from '@/api/admin';
import {
  useReplayableProjections,
  useReplayExecute,
  useReplayHistory,
  useReplayPreview,
  type ReplayScope,
} from '@/api/adminEventReplay';
import DateTime from '@/components/layout/DateTime.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const allTypes = 'all';

const { t } = useI18n();
const { can } = usePlatformCan();

const projectionName = useLocalStorage<string>('nnz.adminEventReplay.projection', '');
const broadcasterId = ref('');
const eventType = ref(allTypes);
const from = ref('');
const to = ref('');
const confirming = ref(false);
const failure = ref<string | null>(null);
const outcome = ref<string | null>(null);
const counted = ref<{ key: string; count: number } | null>(null);

const projections = useReplayableProjections();
const tenants = useAdminTenants({ page: 1, take: 100 });
const history = useReplayHistory();
const previewing = useReplayPreview();
const replaying = useReplayExecute();

const historyState = usePanelState(history, (rows) =>
  rows.length === 0);

const eventTypes = computed(() => {
  const chosen = projections.data.value?.find(
    (entry) => entry.projectionName === projectionName.value,
  );

  return chosen?.subscribedEventTypes ?? [];
});

const scope = computed<ReplayScope | null>(() => {
  if (
    broadcasterId.value === '' ||
    projectionName.value === '' ||
    from.value === '' ||
    to.value === ''
  )
    return null;

  return {
    broadcasterId: broadcasterId.value,
    projectionName: projectionName.value,
    fromUtc: new Date(from.value).toISOString(),
    toUtc: new Date(to.value).toISOString(),
    eventType: eventType.value === allTypes
      ? null
      : eventType.value,
  };
});

// A count belongs to the exact scope it was taken for; any change to the scope drops it.
const confirmedCount = computed<number | null>(() => {
  const current = scope.value;

  if (current === null || counted.value === null)
    return null;

  return counted.value.key === JSON.stringify(current)
    ? counted.value.count
    : null;
});

const channelName = computed(() => {
  const match = tenants.data.value?.data?.find((entry) => entry.id === broadcasterId.value);

  return match?.name ?? broadcasterId.value;
});

const consequence = computed(() =>
  t('dashboard.admin.eventReplay.confirm.consequence', {
    count: confirmedCount.value ?? 0,
    projection: projectionName.value,
    channel: channelName.value,
    from: from.value,
    to: to.value,
  }),
);

async function runPreview(): Promise<void> {
  const current = scope.value;

  if (current === null) return;

  outcome.value = null;
  failure.value = null;

  try {
    const result = await previewing.mutateAsync(current);

    counted.value = {
      key: JSON.stringify(current),
      count: Number(result.matchingEventCount),
    };
  } catch {
    counted.value = null;
    failure.value = t('dashboard.admin.eventReplay.preview.failed');
  }
}

function openConfirm(): void {
  failure.value = null;
  confirming.value = true;
}

function close(): void {
  confirming.value = false;
}

async function submit(): Promise<void> {
  const current = scope.value;
  const expectedCount = confirmedCount.value;

  if (current === null || expectedCount === null) return;

  try {
    const result = await replaying.mutateAsync({ scope: current, expectedCount });

    outcome.value = t('dashboard.admin.eventReplay.done', {
      count: Number(result.appliedCount),
      projection: result.projectionName,
    });

    counted.value = null;
    confirming.value = false;
  } catch {
    failure.value = t('dashboard.admin.eventReplay.failed');
  }
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.admin.eventReplay.title')"
    :crumb="$t('dashboard.admin.crumb')"
  />
  <div data-page-body class="flex min-h-0 min-w-0 flex-1 flex-col gap-3 p-6 max-md:p-4">
    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.eventReplay.scope.title')"
      :description="$t('dashboard.admin.eventReplay.scope.description')"
    >
      <div class="flex flex-wrap items-end gap-3">
        <label class="grid gap-1 text-xs text-muted-foreground">
          {{ $t("dashboard.admin.eventReplay.scope.channel") }}
          <select
            v-model="broadcasterId"
            data-tenant
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
          >
            <option value="">{{ $t("dashboard.admin.eventReplay.scope.choose") }}</option>
            <option
              v-for="entry in tenants.data.value?.data ?? []"
              :key="entry.id"
              :value="entry.id"
            >
              {{ entry.name }}
            </option>
          </select>
        </label>
        <label class="grid gap-1 text-xs text-muted-foreground">
          {{ $t("dashboard.admin.eventReplay.scope.projection") }}
          <select
            v-model="projectionName"
            data-projection
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
            @change="eventType = 'all'"
          >
            <option value="">{{ $t("dashboard.admin.eventReplay.scope.choose") }}</option>
            <option
              v-for="entry in projections.data.value ?? []"
              :key="entry.projectionName"
              :value="entry.projectionName"
            >
              {{ entry.projectionName }}
            </option>
          </select>
        </label>
        <label class="grid gap-1 text-xs text-muted-foreground">
          {{ $t("dashboard.admin.eventReplay.scope.eventType") }}
          <select
            v-model="eventType"
            data-event-type
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
          >
            <option value="all">{{ $t("dashboard.admin.eventReplay.scope.allTypes") }}</option>
            <option v-for="entry in eventTypes" :key="entry" :value="entry">{{ entry }}</option>
          </select>
        </label>
        <label class="grid gap-1 text-xs text-muted-foreground">
          {{ $t("dashboard.admin.eventReplay.scope.from") }}
          <input
            v-model="from"
            data-from
            type="datetime-local"
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
          />
        </label>
        <label class="grid gap-1 text-xs text-muted-foreground">
          {{ $t("dashboard.admin.eventReplay.scope.to") }}
          <input
            v-model="to"
            data-to
            type="datetime-local"
            class="h-9 rounded-md border border-input bg-transparent px-3 text-sm text-foreground"
          />
        </label>
      </div>

      <div v-if="can('iam:manage')" class="mt-4 flex flex-wrap items-center gap-3">
        <Button
          variant="outline"
          data-preview
          :disabled="scope === null || previewing.isPending.value"
          @click="runPreview"
        >{{ $t("dashboard.admin.eventReplay.preview.action") }}</Button>
        <Button
          data-replay
          :disabled="confirmedCount === null || confirmedCount === 0"
          @click="openConfirm"
        >{{ $t("dashboard.admin.eventReplay.replay.action") }}</Button>
        <span v-if="confirmedCount !== null" data-count class="text-sm">{{
          $t("dashboard.admin.eventReplay.preview.count", { count: confirmedCount })
        }}</span>
        <span v-if="failure !== null && !confirming" class="text-sm text-destructive">{{
          failure
        }}</span>
        <span v-if="outcome !== null" data-outcome class="text-sm">{{ outcome }}</span>
      </div>
    </PanelCard>

    <PanelCard
      tier="quiet"
      :title="$t('dashboard.admin.eventReplay.history.title')"
      :description="$t('dashboard.admin.eventReplay.history.description')"
      :state="historyState"
      :empty-text="$t('dashboard.admin.eventReplay.history.empty')"
      :error-text="$t('dashboard.admin.eventReplay.history.error')"
      @retry="history.refetch()"
    >
      <ul class="divide-y divide-border">
        <li
          v-for="row in history.data.value ?? []"
          :key="String(row.id)"
          data-replay-row
          class="flex flex-wrap items-center gap-3 py-3"
        >
          <div class="min-w-0 flex-1">
            <span class="block text-sm font-medium">{{ row.targetResource }}</span>
            <span class="block text-xs text-muted-foreground">{{ row.justification }}</span>
            <span class="block text-xs text-muted-foreground"><DateTime :value="row.occurredAt" variant="dateTime" /></span>
          </div>
          <Badge variant="outline">{{ row.outcome }}</Badge>
        </li>
      </ul>
    </PanelCard>

    <FormDialog
      :open="confirming"
      :title="$t('dashboard.admin.eventReplay.confirm.title')"
      :description="consequence"
      :action="$t('dashboard.admin.eventReplay.confirm.action')"
      :pending="replaying.isPending.value"
      :error="failure ?? undefined"
      @update:open="close"
      @submit="submit"
    />
  </div>
</template>
