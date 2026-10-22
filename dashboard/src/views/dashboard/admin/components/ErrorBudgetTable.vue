<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One row per tenant: resolved attempts, errors, error rate and how much of the budget is left.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenantErrorBudget } from '@/api/adminErrorBudget';
import { Badge } from '@/components/ui/badge';
import { Progress } from '@/components/ui/progress';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ rows: readonly AdminTenantErrorBudget[] }>();

const { locale } = useI18n();

const counts = computed<Intl.NumberFormat>(() => new Intl.NumberFormat(locale.value));
const percents = computed<Intl.NumberFormat>(
  () => new Intl.NumberFormat(locale.value, { style: 'percent', maximumFractionDigits: 1 }),
);

function count(value: number | string): string {
  return counts.value.format(Number(value));
}

function rate(row: AdminTenantErrorBudget): string | null {
  return row.errorRate === null || row.errorRate === undefined
    ? null
    : percents.value.format(Number(row.errorRate));
}

function remaining(row: AdminTenantErrorBudget): number | null {
  if (row.budgetRemainingFraction === null || row.budgetRemainingFraction === undefined)
    return null;

  const fraction = Number(row.budgetRemainingFraction);
  const capped = Math.min(1, fraction);

  return Math.max(0, capped);
}

function isExhausted(row: AdminTenantErrorBudget): boolean {
  const left = remaining(row);

  return left !== null && left <= 0;
}

function remainingText(row: AdminTenantErrorBudget): string {
  return percents.value.format(remaining(row) ?? 0);
}

function remainingPercent(row: AdminTenantErrorBudget): number {
  return (remaining(row) ?? 0) * 100;
}
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t("dashboard.admin.errorBudget.columns.channel") }}</TableHead>
        <TableHead class="text-right">
          {{ $t("dashboard.admin.errorBudget.columns.attempts") }}
        </TableHead>
        <TableHead class="text-right">
          {{ $t("dashboard.admin.errorBudget.columns.errors") }}
        </TableHead>
        <TableHead class="text-right">
          {{ $t("dashboard.admin.errorBudget.columns.rate") }}
        </TableHead>
        <TableHead class="w-56">{{ $t("dashboard.admin.errorBudget.columns.budget") }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="row in rows" :key="row.broadcasterId" data-budget-row>
        <TableCell stack="title" class="font-medium">{{ row.channelDisplayName }}</TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.errorBudget.columns.attempts')">{{ count(row.attempts) }}</TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.errorBudget.columns.errors')">{{ count(row.errors) }}</TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.errorBudget.columns.rate')">
          <template v-if="rate(row) !== null">{{ rate(row) }}</template>
          <span v-else class="text-muted-foreground">
            {{ $t("dashboard.admin.errorBudget.noAttempts") }}
          </span>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.errorBudget.columns.budget')">
          <div v-if="remaining(row) !== null" class="flex items-center gap-3">
            <Progress :model-value="remainingPercent(row)" class="w-24" />
            <Badge v-if="isExhausted(row)" variant="destructive" data-exhausted>
              {{ $t("dashboard.admin.errorBudget.exhausted") }}
            </Badge>
            <span v-else class="text-sm tabular-nums">
              {{ $t("dashboard.admin.errorBudget.left", { share: remainingText(row) }) }}
            </span>
          </div>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
