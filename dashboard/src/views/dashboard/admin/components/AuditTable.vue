<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One row per audit entry, newest first: when, who, what permission, which tenant and what the answer was.
import type { AdminAuditEntry } from '@/api/adminAudit';
import DateTime from '@/components/layout/DateTime.vue';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ rows: readonly AdminAuditEntry[] }>();

</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t("dashboard.admin.audit.columns.when") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.audit.columns.principal") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.audit.columns.permission") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.audit.columns.target") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.audit.columns.justification") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.audit.columns.outcome") }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="row in rows" :key="row.id" data-audit-row>
        <TableCell stack="title" class="whitespace-nowrap tabular-nums max-md:font-medium"><DateTime :value="row.occurredAt" variant="dateTimeFull" /></TableCell>
        <TableCell :label="$t('dashboard.admin.audit.columns.principal')">
          <span class="text-sm">{{ row.principalType }}</span>
        </TableCell>
        <TableCell class="font-mono text-xs max-md:text-sm max-md:break-all" :label="$t('dashboard.admin.audit.columns.permission')">{{ row.permission }}</TableCell>
        <TableCell class="text-sm" :label="$t('dashboard.admin.audit.columns.target')">
          <span v-if="row.targetResource" class="break-all">{{ row.targetResource }}</span>
        </TableCell>
        <TableCell class="max-w-64 truncate text-sm max-md:max-w-none max-md:overflow-visible max-md:whitespace-normal" :label="$t('dashboard.admin.audit.columns.justification')">{{ row.justification }}</TableCell>
        <TableCell :label="$t('dashboard.admin.audit.columns.outcome')">
          <div class="flex items-center gap-2">
            <Badge :variant="row.outcome === 'Denied' ? 'destructive' : 'secondary'" data-outcome>
              {{ $t(`dashboard.admin.audit.outcome.${row.outcome}`) }}
            </Badge>
            <Badge v-if="row.breakGlass" variant="outline" data-break-glass>
              {{ $t("dashboard.admin.audit.breakGlass") }}
            </Badge>
          </div>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
