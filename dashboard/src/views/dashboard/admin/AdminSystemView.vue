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
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { usePanelState } from '@/composables/usePanelState';
import { usePlatformCan } from '@/composables/usePlatformCan';

const SECONDS_PER_MINUTE: number = 60;
const SECONDS_PER_HOUR: number = 3600;
const SECONDS_PER_DAY: number = 86_400;

type BadgeVariant = 'outline' | 'secondary' | 'destructive';

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

function statusVariant(value: string): BadgeVariant {
  if (value === 'healthy')
    return 'outline';

  return value === 'degraded'
    ? 'secondary'
    : 'destructive';
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
  <PageBody>
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
          <Badge data-overall :variant="statusVariant(system.overall)">{{
            statusLabel(system.overall)
          }}</Badge>
        </div>
        <ul class="divide-y divide-border">
          <li
            v-for="service in system?.services ?? []"
            :key="service.name"
            data-service-row
            class="flex items-center gap-3 py-3"
          >
            <div class="min-w-0 flex-1">
              <div data-service-name class="text-sm font-medium">
                {{ checkName(service) }}
              </div>
              <div
                v-if="checkMeaning(service)"
                data-service-meaning
                class="text-sm text-muted-foreground"
              >
                {{ checkMeaning(service) }}
              </div>
            </div>
            <span v-if="latency(service)" class="text-xs text-muted-foreground tabular-nums">{{
              latency(service)
            }}</span>
            <Badge :variant="statusVariant(service.status)">{{
              statusLabel(service.status)
            }}</Badge>
          </li>
        </ul>
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
          <ul class="mt-2 divide-y divide-border">
            <li
              v-for="row in outcome.failures"
              :key="String(row.cryptoKeyId)"
              data-rotation-failure
              class="flex flex-wrap items-center gap-3 py-2 text-sm"
            >
              <code class="text-xs text-muted-foreground">{{ row.cryptoKeyId }}</code>
              <span>{{ row.reason }}</span>
            </li>
          </ul>
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
      <label class="grid gap-1 text-xs text-muted-foreground">
        {{ $t("dashboard.admin.system.rotation.dialog.previous") }}
        <Input
          v-model="previousKey"
          data-previous-key
          type="password"
          autocomplete="off"
          spellcheck="false"
        />
      </label>
      <label class="grid gap-1 text-xs text-muted-foreground">
        {{ $t("dashboard.admin.system.rotation.dialog.current") }}
        <Input
          v-model="currentKey"
          data-current-key
          type="password"
          autocomplete="off"
          spellcheck="false"
        />
      </label>
    </FormDialog>
  </PageBody>
</template>
