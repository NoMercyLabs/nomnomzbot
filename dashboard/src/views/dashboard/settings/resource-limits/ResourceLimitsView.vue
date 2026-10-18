<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What the channel may use and how much of it is in use: every limited resource with its count, the plan
// behind the limits, and the metered usage of this period. The limits only change with the plan (Billing).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { useEntitlement, useResourceLimits, useUsageMetrics, type UsageMetric } from '@/api/resourceLimits';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import LimitMeter from './components/LimitMeter.vue';
import { groupLimits, metricRowOf, type MetricRow } from './limitRows';

const { t, te, d } = useI18n();
const router = useRouter();
const { channelId } = useCurrentChannel();

const limits = useResourceLimits(channelId);
const usage = useUsageMetrics(channelId);
const plan = useEntitlement(channelId);

const groups = computed(() => groupLimits(limits.data.value ?? []));
const metrics = computed(() => (usage.data.value ?? []).map(metricRowOf));
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

  return te(path)
    ? t(path)
    : row.key;
}

function yesNo(value: boolean | undefined): string {
  return value === true
    ? t('dashboard.settings.resourceLimits.plan.yes')
    : t('dashboard.settings.resourceLimits.plan.no');
}

function baselineDetail(baseline: number): string {
  return t('dashboard.settings.resourceLimits.limits.baseline', { value: baseline });
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
          <section v-if="groups.costDriving.length > 0" data-group="cost-driving" class="grid gap-2">
            <h3 class="text-sm font-medium">{{ $t('dashboard.settings.resourceLimits.limits.costDriving.title') }}</h3>
            <p class="text-sm text-muted-foreground">
              {{ $t('dashboard.settings.resourceLimits.limits.costDriving.description') }}
            </p>
            <RowGroup>
              <LimitMeter
                v-for="row in groups.costDriving"
                :key="row.key"
                :label="row.label"
                :used="row.used"
                :limit="row.limit"
                :percent="row.percent"
                :level="row.level"
              />
            </RowGroup>
          </section>
          <section v-if="groups.nearFree.length > 0" data-group="near-free" class="grid gap-2">
            <h3 class="text-sm font-medium">{{ $t('dashboard.settings.resourceLimits.limits.nearFree.title') }}</h3>
            <p class="text-sm text-muted-foreground">
              {{ $t('dashboard.settings.resourceLimits.limits.nearFree.description') }}
            </p>
            <RowGroup>
              <LimitMeter
                v-for="row in groups.nearFree"
                :key="row.key"
                :label="row.label"
                :used="row.used"
                :limit="row.limit"
                :percent="row.percent"
                :level="row.level"
                :detail="baselineDetail(row.baseline)"
              />
            </RowGroup>
          </section>
        </div>
      </PanelCard>

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
            <dd data-plan="tier" class="font-medium">{{ plan.data.value.tierKey }}</dd>
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
            <Button v-if="billingLinked" size="sm" as-child>
              <RouterLink :to="{ name: 'dashboard-settings-billing' }">
                {{ $t('dashboard.settings.resourceLimits.plan.change') }}
              </RouterLink>
            </Button>
          </div>
        </template>
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
