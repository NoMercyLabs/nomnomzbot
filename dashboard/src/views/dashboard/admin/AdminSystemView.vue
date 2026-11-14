<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// System: the health of every service the bot probes, the running process, and the encryption key
// rotation. The status is one snapshot that the admin hub replaces every 15 seconds. The rotation is
// the only action: a confirm dialog names what it changes, and the typed keys leave the page when the
// dialog closes or the rotation succeeds. The server never sends a key back.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Locked from '~icons/nnz/locked';

import {
  useAdminSystemStatus,
  useRotateEncryptionKey,
  type KeyRotationSummary,
  type ServiceHealth,
} from '@/api/adminSystem';
import FormDialog from '@/components/layout/FormDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';
import UpdateCard from './components/UpdateCard.vue';

const SECONDS_PER_MINUTE: number = 60;
const SECONDS_PER_HOUR: number = 3600;
const SECONDS_PER_DAY: number = 86_400;

// A check slower than this is worth a number; a fast one is just healthy.
const SLOW_CHECK_MS: number = 500;
const MS_PER_SECOND: number = 1000;

const { t, te, n } = useI18n();
const { can } = usePlatformCan();

const status = useAdminSystemStatus();
const rotating = useRotateEncryptionKey();

const previousKey = ref('');
const currentKey = ref('');
const confirming = ref(false);
const failure = ref<string | null>(null);
const outcome = ref<KeyRotationSummary | null>(null);

const panelState = usePanelState(status, (data) =>
  data.system.services.length === 0);

const keysMissing = computed<boolean>(() => previousKey.value === '' || currentKey.value === '');

const system = computed(() => status.data.value?.system);
const stats = computed(() => status.data.value?.stats ?? null);

const uptime = computed<string>(() => {
  const seconds = Number(stats.value?.botUptimeSeconds ?? 0);
  const days = Math.floor(seconds / SECONDS_PER_DAY);
  const hours = Math.floor((seconds % SECONDS_PER_DAY) / SECONDS_PER_HOUR);
  const minutes = Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE);

  if (days > 0)
    return t('dashboard.admin.system.process.uptimeDays', { days, hours });

  if (hours > 0)
    return t('dashboard.admin.system.process.uptimeHours', { hours, minutes });

  return minutes > 0
    ? t('dashboard.admin.system.process.uptimeMinutes', { minutes })
    : t('dashboard.admin.system.process.uptimeUnderMinute');
});

const memory = computed<string>(() =>
  t('dashboard.admin.system.process.memoryValue', { mb: Number(system.value?.memoryUsageMb ?? 0) }),
);

const processor = computed<string>(() =>
  t('dashboard.admin.system.process.cpuValue', {
    percent: Number(system.value?.cpuPercent ?? 0).toFixed(1),
  }),
);

const channelsContext = computed<string>(() =>
  t('dashboard.admin.system.process.channelsContext', {
    total: Number(stats.value?.totalChannels ?? 0),
  }),
);

const outcomeText = computed<string>(() => {
  const summary = outcome.value;

  if (summary === null)
    return '';

  return t('dashboard.admin.system.rotation.outcome', {
    rewrapped: Number(summary.rewrappedCount),
    current: Number(summary.alreadyCurrentCount),
    failed: summary.failures.length,
  });
});

function statusLabel(value: string): string {
  return t(`dashboard.admin.system.status.${value}`, value);
}

function statusTone(value: string): StatusTone {
  if (value === 'healthy')
    return 'ok';

  return value === 'degraded'
    ? 'attention'
    : 'bad';
}

function latency(service: ServiceHealth): string | null {
  const ms = Number(service.latencyMs ?? 0);

  if (ms < SLOW_CHECK_MS)
    return null;

  return t('dashboard.admin.system.health.latency', {
    seconds: n(ms / MS_PER_SECOND, { maximumFractionDigits: 1 }),
  });
}

function checkName(service: ServiceHealth): string {
  return te(`dashboard.admin.system.checks.${service.name}.name`)
    ? t(`dashboard.admin.system.checks.${service.name}.name`)
    : service.name;
}

function checkMeaning(service: ServiceHealth): string | null {
  return te(`dashboard.admin.system.checks.${service.name}.meaning`)
    ? t(`dashboard.admin.system.checks.${service.name}.meaning`)
    : null;
}

function clearKeys(): void {
  previousKey.value = '';
  currentKey.value = '';
}

function openConfirm(): void {
  failure.value = null;
  outcome.value = null;
  confirming.value = true;
}

function close(): void {
  confirming.value = false;
  failure.value = null;
  clearKeys();
}

async function submit(): Promise<void> {
  try {
    outcome.value = await rotating.mutateAsync({
      previousKey: previousKey.value,
      currentKey: currentKey.value,
    });

    close();
  } catch {
    failure.value = t('dashboard.admin.system.rotation.failed');
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.system.title')" :crumb="$t('dashboard.admin.crumb')" />
  <PageBody data-page-body>
    <div class="flex flex-none flex-col gap-4">
      <PanelCard
        tier="lead"
        pad
        :title="$t('dashboard.admin.system.health.title')"
        :description="$t('dashboard.admin.system.health.description')"
        :state="panelState"
        :empty-text="$t('dashboard.admin.system.health.empty')"
        :error-text="$t('dashboard.admin.system.health.error')"
        @retry="status.refetch()"
      >
        <div v-if="system" class="mb-3 flex items-center gap-2 text-sm text-muted-foreground">
          {{ $t("dashboard.admin.system.health.overall") }}
          <StatusBadge
            data-overall
            :tone="statusTone(system.overall)"
            :label="statusLabel(system.overall)"
          />
        </div>
        <RowGroup>
          <SummaryRow
            v-for="service in system?.services ?? []"
            :key="service.name"
            data-service-row
            :title="checkName(service)"
          >
            <template #title>
              <span data-service-name>{{ checkName(service) }}</span>
            </template>
            <template v-if="checkMeaning(service)" #meta>
              <span data-service-meaning class="text-sm">{{ checkMeaning(service) }}</span>
            </template>
            <template #end>
              <span v-if="latency(service)" class="text-xs text-muted-foreground tabular-nums">{{
                latency(service)
              }}</span>
              <StatusBadge
                :tone="statusTone(service.status)"
                :label="statusLabel(service.status)"
              />
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>

      <PanelCard
        v-if="system"
        tier="default"
        pad
        :title="$t('dashboard.admin.system.process.title')"
        :description="$t('dashboard.admin.system.process.description')"
      >
        <StatStrip>
          <StatTile
            :label="$t('dashboard.admin.system.process.version')"
            :value="system.botVersion"
          />
          <StatTile :label="$t('dashboard.admin.system.process.memory')" :value="memory" />
          <StatTile :label="$t('dashboard.admin.system.process.cpu')" :value="processor" />
        </StatStrip>
      </PanelCard>

      <UpdateCard v-if="system" :bot-version="system.botVersion" />

      <PanelCard
        v-if="system"
        tier="default"
        pad
        :title="$t('dashboard.admin.system.totals.title')"
        :description="$t('dashboard.admin.system.totals.description')"
      >
        <StatStrip v-if="stats" data-totals>
          <StatTile :label="$t('dashboard.admin.system.process.uptime')" :value="uptime" />
          <StatTile
            :label="$t('dashboard.admin.system.process.events')"
            :value="Number(stats.eventsProcessedToday)"
          />
          <StatTile
            :label="$t('dashboard.admin.system.process.channels')"
            :value="Number(stats.activeChannels)"
            :context="channelsContext"
          />
          <StatTile
            :label="$t('dashboard.admin.system.process.users')"
            :value="Number(stats.totalUsers)"
          />
        </StatStrip>
        <p v-else data-totals-locked class="flex items-center gap-2 text-sm text-muted-foreground">
          <Locked class="size-5 shrink-0" aria-hidden="true" />
          {{ $t("dashboard.admin.system.totals.locked") }}
        </p>
      </PanelCard>

      <PanelCard
        tier="quiet"
        pad
        :title="$t('dashboard.admin.system.rotation.title')"
        :description="$t('dashboard.admin.system.rotation.description')"
      >
        <div class="flex flex-wrap items-center gap-3">
          <Button v-if="can('iam:manage')" variant="destructive" data-rotate @click="openConfirm">{{
            $t("dashboard.admin.system.rotation.open")
          }}</Button>
          <span v-if="outcome !== null" data-rotation-outcome class="text-sm">{{
            outcomeText
          }}</span>
        </div>
        <div v-if="outcome !== null && outcome.failures.length > 0" class="mt-4">
          <span class="text-sm font-medium">{{
            $t("dashboard.admin.system.rotation.failures")
          }}</span>
          <RowGroup class="mt-2">
            <SummaryRow
              v-for="row in outcome.failures"
              :key="String(row.cryptoKeyId)"
              data-rotation-failure
              :title="row.reason"
              :meta="String(row.cryptoKeyId)"
            />
          </RowGroup>
        </div>
      </PanelCard>
    </div>

    <FormDialog
      :open="confirming"
      :title="$t('dashboard.admin.system.rotation.dialog.title')"
      :description="$t('dashboard.admin.system.rotation.dialog.description')"
      :action="$t('dashboard.admin.system.rotation.dialog.action')"
      :pending="rotating.isPending.value"
      :disabled="keysMissing"
      :error="failure ?? undefined"
      @update:open="close"
      @submit="submit"
    >
      <Field class="gap-1">
        <FieldLabel for="rotation-previous" class="text-xs font-normal text-muted-foreground">
          {{ $t("dashboard.admin.system.rotation.dialog.previous") }}
        </FieldLabel>
        <Input
          id="rotation-previous"
          v-model="previousKey"
          data-previous-key
          type="password"
          autocomplete="off"
          spellcheck="false"
        />
      </Field>
      <Field class="gap-1">
        <FieldLabel for="rotation-current" class="text-xs font-normal text-muted-foreground">
          {{ $t("dashboard.admin.system.rotation.dialog.current") }}
        </FieldLabel>
        <Input
          id="rotation-current"
          v-model="currentKey"
          data-current-key
          type="password"
          autocomplete="off"
          spellcheck="false"
        />
      </Field>
    </FormDialog>
  </PageBody>
</template>
