<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One row per tenant for its latest metering period. A unit with no price is named, never shown as free.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenantUsage } from '@/api/adminUsage';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useTime } from '@/composables/useTime';

defineProps<{ rows: readonly AdminTenantUsage[] }>();

const { locale } = useI18n();

const time = useTime();
const counts = computed<Intl.NumberFormat>(() => new Intl.NumberFormat(locale.value));

function period(row: AdminTenantUsage): string {
  const start = time.date(row.periodStart);
  const end = time.date(row.periodEnd);

  return `${start} - ${end}`;
}

function count(value: number | string): string {
  return counts.value.format(Number(value));
}

function money(minorUnits: number | string, currency: string): string {
  const format = new Intl.NumberFormat(locale.value, { style: 'currency', currency });

  return format.format(Number(minorUnits) / 100);
}

function unpricedList(row: AdminTenantUsage): string {
  return (row.unpricedUnitKeys ?? []).join(', ');
}
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t("dashboard.admin.tenantUsage.columns.channel") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.tenantUsage.columns.period") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.tenantUsage.columns.metrics") }}</TableHead>
        <TableHead class="text-right">
          {{ $t("dashboard.admin.tenantUsage.columns.tts") }}
        </TableHead>
        <TableHead class="text-right">
          {{ $t("dashboard.admin.tenantUsage.columns.cost") }}
        </TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="row in rows" :key="row.broadcasterId" data-usage-row>
        <TableCell stack="title" class="font-medium">{{ row.channelDisplayName }}</TableCell>
        <TableCell class="text-muted-foreground" :label="$t('dashboard.admin.tenantUsage.columns.period')">{{ period(row) }}</TableCell>
        <TableCell :label="$t('dashboard.admin.tenantUsage.columns.metrics')">
          <div class="min-w-0">
            <ul v-if="row.metrics.length > 0" class="flex flex-col gap-0.5 text-sm">
              <li v-for="metric in row.metrics" :key="metric.metricKey" class="tabular-nums">
                <span class="text-muted-foreground">{{ metric.metricKey }}</span>
                {{ count(metric.quantity) }}
              </li>
            </ul>
            <span v-else class="text-muted-foreground">
              {{ $t("dashboard.admin.tenantUsage.noUsage") }}
            </span>
            <p
              v-if="(row.unpricedUnitKeys?.length ?? 0) > 0"
              data-unpriced
              class="mt-1 text-xs text-muted-foreground"
            >
              {{ $t("dashboard.admin.tenantUsage.unpriced", { units: unpricedList(row) }) }}
            </p>
          </div>
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.tenantUsage.columns.tts')">{{ count(row.ttsCharacterCount) }}</TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.tenantUsage.columns.cost')">
          <div v-if="(row.totalCostsByCurrency?.length ?? 0) > 0">
            <div v-for="total in row.totalCostsByCurrency" :key="total.currency">
              {{ money(total.minorUnits, total.currency) }}
            </div>
          </div>
          <span v-else class="text-muted-foreground">
            {{ $t("dashboard.admin.tenantUsage.noCost") }}
          </span>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
