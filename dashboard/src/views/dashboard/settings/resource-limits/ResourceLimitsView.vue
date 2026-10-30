<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What the channel may use and how much of it is in use: every limited resource with its count, the plan
// behind the limits, and the metered usage of this period. The limits only change with the plan (Billing).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { tierNameOf, useBillingTiers } from '@/api/billing';
import { useEntitlement, useResourceLimits, useUsageMetrics, type UsageMetric } from '@/api/resourceLimits';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import LimitMeter from './components/LimitMeter.vue';
import LimitSection from './components/LimitSection.vue';
import { groupLimits, isMetered, metricRowOf, type MetricRow } from './limitRows';

const { t, te, d } = useI18n();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const limits = useResourceLimits(channelId);
const usage = useUsageMetrics(channelId);
const plan = useEntitlement(channelId);
const tiers = useBillingTiers(channelId);

const planName = computed(() =>
  tierNameOf(tiers.data.value, plan.data.value?.tierKey) ??
  t('dashboard.settings.resourceLimits.plan.unnamed'));

const groups = computed(() => groupLimits(limits.data.value ?? []));
// A metric that repeats a limit is left out: the limit above already shows that resource, once, with its count.
const limitKeys = computed(() => new Set((limits.data.value ?? []).map((item) => item.limitKey)));
const metrics = computed(() => (usage.data.value ?? [])
  .filter((item) => isMetered(item.metricKey) && !limitKeys.value.has(item.metricKey))
  .map(metricRowOf));
const period = computed(() => periodOf(usage.data.value ?? []));
const billingLinked = computed(() => router.hasRoute('dashboard-settings-billing'));

type QueryFlags = { isError: { value: boolean }; isPending: { value: boolean } };

function panelState(query: QueryFlags, empty: boolean): PanelState {
  if (query.isError.value)
    return 'error';

  if (query.isPending.value)
    return 'loading';

  return empty
    ? 'empty'
    : 'ready';
}

const limitsState = computed(() => panelState(limits, (limits.data.value ?? []).length === 0));
const usageState = computed(() => panelState(usage, metrics.value.length === 0));
const planState = computed(() => panelState(plan, false));

function periodOf(items: readonly UsageMetric[]): string {
  const first = items[0];

  if (first === undefined)
    return '';

  const style = { dateStyle: 'medium' } as const;

  return t('dashboard.settings.resourceLimits.usage.period', {
    from: d(new Date(first.periodStart), style),
    to: d(new Date(first.periodEnd), style),
  });
}

// A metric the page has a word for shows that word; a new one shows its key until a word is added.
function metricLabel(row: MetricRow): string {
  const path = `dashboard.settings.resourceLimits.metrics.${row.key}`;
  const limitPath = `dashboard.settings.resourceLimits.limits.names.${row.key}`;

  if (te(path))
    return t(path);

  return te(limitPath)
    ? t(limitPath)
    : row.key;
}

function yesNo(value: boolean | undefined): string {
  return value === true
    ? t('dashboard.settings.resourceLimits.plan.yes')
    : t('dashboard.settings.resourceLimits.plan.no');
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.resourceLimits')"
    :crumb="$t('dashboard.settings.resourceLimits.crumb')"
  />

  <PageBody>
    <div data-view="resource-limits" class="grid gap-4">
      <PanelCard
        tier="quiet"
        pad
        :title="$t('dashboard.settings.resourceLimits.plan.title')"
        :description="$t('dashboard.settings.resourceLimits.plan.description')"
        :state="planState"
        :error-text="$t('dashboard.settings.resourceLimits.plan.error')"
        @retry="plan.refetch()"
      >
        <dl v-if="plan.data.value" class="grid gap-2 text-sm">
          <div class="flex items-baseline justify-between gap-4">
            <dt class="text-muted-foreground">{{ $t('dashboard.settings.resourceLimits.plan.tier') }}</dt>
            <dd data-plan="tier" class="font-medium">{{ planName }}</dd>
          </div>
          <div class="flex items-baseline justify-between gap-4">
            <dt class="text-muted-foreground">{{ $t('dashboard.settings.resourceLimits.plan.customBotName') }}</dt>
            <dd data-plan="custom-bot-name" class="font-medium">{{ yesNo(plan.data.value.allowsCustomBotName) }}</dd>
          </div>
          <div class="flex items-baseline justify-between gap-4">
            <dt class="text-muted-foreground">{{ $t('dashboard.settings.resourceLimits.plan.prioritySupport') }}</dt>
            <dd data-plan="priority-support" class="font-medium">{{ yesNo(plan.data.value.prioritySupport) }}</dd>
          </div>
        </dl>
        <template #footer>
          <div class="flex w-full flex-wrap items-center justify-between gap-2">
            <p data-restricted class="min-w-0 flex-1 basis-64 text-sm text-muted-foreground">
              {{ $t('dashboard.settings.resourceLimits.plan.restricted') }}
            </p>
            <Button v-if="billingLinked && can('billing:read')" size="sm" as-child>
              <RouterLink :to="{ name: 'dashboard-settings-billing' }">
                {{ $t('dashboard.settings.resourceLimits.plan.change') }}
              </RouterLink>
            </Button>
          </div>
        </template>
      </PanelCard>

      <PanelCard
        tier="lead"
        pad
        :title="$t('dashboard.settings.resourceLimits.limits.title')"
        :description="$t('dashboard.settings.resourceLimits.limits.description')"
        :state="limitsState"
        :empty-text="$t('dashboard.settings.resourceLimits.limits.empty')"
        :error-text="$t('dashboard.settings.resourceLimits.limits.error')"
        @retry="limits.refetch()"
      >
        <div class="grid gap-4">
          <LimitSection
            v-if="groups.storage.length > 0"
            group="storage"
            :title="$t('dashboard.settings.resourceLimits.limits.storage.title')"
            :description="$t('dashboard.settings.resourceLimits.limits.storage.description')"
            :rows="groups.storage"
          />
          <LimitSection
            v-if="groups.allowance.length > 0"
            group="allowance"
            :title="$t('dashboard.settings.resourceLimits.limits.allowance.title')"
            :description="$t('dashboard.settings.resourceLimits.limits.allowance.description')"
            :rows="groups.allowance"
          />
          <LimitSection
            v-if="groups.perMessage.length > 0"
            group="per-message"
            per-message
            :title="$t('dashboard.settings.resourceLimits.limits.perMessage.title')"
            :description="$t('dashboard.settings.resourceLimits.limits.perMessage.description')"
            :rows="groups.perMessage"
          />
          <LimitSection
            v-if="groups.nearFree.length > 0"
            group="near-free"
            show-baseline
            :title="$t('dashboard.settings.resourceLimits.limits.nearFree.title')"
            :description="$t('dashboard.settings.resourceLimits.limits.nearFree.description')"
            :rows="groups.nearFree"
          />
        </div>
      </PanelCard>

      <PanelCard
        tier="quiet"
        pad
        :title="$t('dashboard.settings.resourceLimits.usage.title')"
        :description="period || $t('dashboard.settings.resourceLimits.usage.description')"
        :state="usageState"
        :empty-text="$t('dashboard.settings.resourceLimits.usage.empty')"
        :error-text="$t('dashboard.settings.resourceLimits.usage.error')"
        @retry="usage.refetch()"
      >
        <RowGroup>
          <LimitMeter
            v-for="row in metrics"
            :key="row.key"
            :label="metricLabel(row)"
            :unit="row.unit"
            :used="row.used"
            :limit="row.limit"
            :percent="row.percent"
            :level="row.level"
          />
        </RowGroup>
      </PanelCard>
    </div>
  </PageBody>
</template>
