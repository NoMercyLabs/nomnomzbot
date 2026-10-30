<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Summary tab of a channel console: how the channel's runs went (the diagnostics summary: counts of the
// last 24 hours and the newest failure of all time) and the facts the bot holds about the channel. Hosting a
// channel for a third party is the restricted saas mode, so the tab always carries that restriction.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenantDetail } from '@/api/admin';
import { useChannelDiagnostics } from '@/api/adminDiagnostics';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const props = defineProps<{ tenant: AdminTenantDetail }>();

const { t, te } = useI18n();
const time = useTime();

const diagnostics = useChannelDiagnostics(() => props.tenant.id, ['summary']);
const summary = computed(() => diagnostics.data.value?.summary ?? null);
const healthState = usePanelState(diagnostics, (data) =>
  data === null);

const runs = computed<number>(() => Number(summary.value?.runsLast24Hours ?? 0));
const failed = computed<number>(() => Number(summary.value?.failedRunsLast24Hours ?? 0));
const lastError = computed<string>(() => summary.value?.lastFailedRunError ?? '');

const failedAt = computed<string>(() => {
  const at = summary.value?.lastFailedRunAt;

  return at
    ? t('dashboard.admin.console.summary.failedAt', { when: time.date(at) })
    : '';
});

const modeName = computed<string>(() => {
  const mode = props.tenant.deploymentMode;

  return te(`dashboard.admin.detail.modes.${mode}`)
    ? t(`dashboard.admin.detail.modes.${mode}`)
    : mode;
});

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.detail.owner'), value: props.tenant.ownerDisplayName },
  { label: t('dashboard.admin.detail.status'), value: t(`dashboard.admin.status.${props.tenant.status}`) },
  {
    label: t('dashboard.admin.detail.live'),
    value: summary.value?.isLive
      ? t('dashboard.admin.live.live')
      : t('dashboard.admin.live.offline'),
  },
  { label: t('dashboard.admin.detail.tier'), value: summary.value?.tierDisplayName },
  { label: t('dashboard.admin.detail.mode'), value: modeName.value },
  { label: t('dashboard.admin.detail.members'), value: Number(props.tenant.membershipCount) },
  { label: t('dashboard.admin.detail.created'), value: time.date(props.tenant.createdAt) },
  { label: t('dashboard.admin.detail.suspendedReason'), value: props.tenant.suspendedReason },
]);
</script>

<template>
  <div class="grid gap-3">
    <PanelCard
      pad
      :title="t('dashboard.admin.console.summary.healthTitle')"
      :description="t('dashboard.admin.console.summary.healthDescription')"
      :state="healthState"
      :empty-text="t('dashboard.admin.console.summary.error')"
      :error-text="t('dashboard.admin.console.summary.error')"
      @retry="diagnostics.refetch()"
    >
      <div class="grid gap-4">
        <div class="grid grid-cols-2 gap-4 max-md:grid-cols-1">
          <div data-summary-runs class="grid gap-1">
            <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.summary.runs') }}</p>
            <p class="text-2xl font-semibold tabular-nums">{{ runs }}</p>
            <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.summary.runsHint') }}</p>
          </div>
          <div data-summary-failed class="grid gap-1">
            <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.summary.failed') }}</p>
            <p class="text-2xl font-semibold tabular-nums">{{ failed }}</p>
            <StatusBadge
              v-if="failed > 0"
              tone="bad"
              :label="t('dashboard.admin.console.summary.failedHint', { count: failed, total: runs })"
            />
            <p v-else class="text-sm text-muted-foreground">
              {{ t('dashboard.admin.console.summary.noneFailed') }}
            </p>
          </div>
        </div>
        <div data-summary-last-failure class="grid gap-1">
          <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.summary.lastFailure') }}</p>
          <template v-if="lastError">
            <p class="font-mono text-sm wrap-anywhere">{{ lastError }}</p>
            <p class="text-sm text-muted-foreground">{{ failedAt }}</p>
          </template>
          <p v-else class="text-sm text-muted-foreground">
            {{ t('dashboard.admin.console.summary.noFailure') }}
          </p>
        </div>
      </div>
    </PanelCard>
    <PanelCard
      pad
      :title="t('dashboard.admin.console.summary.factsTitle')"
      :description="t('dashboard.admin.console.summary.factsDescription')"
    >
      <div class="grid gap-4">
        <KeyValueList :items="facts" />
        <p data-saas-restricted class="text-sm text-muted-foreground">
          {{ t('dashboard.admin.tenants.restricted') }}
        </p>
      </div>
    </PanelCard>
  </div>
</template>
