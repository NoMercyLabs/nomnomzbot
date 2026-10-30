<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The runs of one channel, newest first. A row opens the run's detail; the time is the button, so the keyboard
// reaches it, and the whole row answers a click. The bot names a person and a pipeline by id only today, so the
// ids show as they are.
import { useI18n } from 'vue-i18n';

import type { ChannelRun } from '@/api/adminDiagnostics';
import DateTime from '@/components/layout/DateTime.vue';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { durationText } from '@/lib/time';

import RunFailureKind from './RunFailureKind.vue';
import RunStatus from './RunStatus.vue';

defineProps<{ rows: readonly ChannelRun[] }>();

defineEmits<{ open: [run: ChannelRun] }>();

const { locale } = useI18n();
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.time') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.trigger') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.pipeline') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.person') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.status') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.console.runs.columns.duration') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.console.runs.columns.hostCalls') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.failure') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.runs.columns.error') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow
        v-for="row in rows"
        :key="String(row.id)"
        data-run-row
        class="cursor-pointer"
        @click="$emit('open', row)"
      >
        <TableCell stack="title" class="font-medium">
          <button type="button" class="text-left underline-offset-4 hover:underline">
            <DateTime :value="row.startedAt" variant="dateTime" />
          </button>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.runs.columns.trigger')">{{ row.triggerKind }}</TableCell>
        <TableCell class="font-mono text-sm wrap-anywhere" :label="$t('dashboard.admin.console.runs.columns.pipeline')">
          {{ row.pipelineId ?? $t('dashboard.admin.console.runs.none') }}
        </TableCell>
        <TableCell class="font-mono text-sm wrap-anywhere" :label="$t('dashboard.admin.console.runs.columns.person')">
          {{ row.triggeredByUserId ?? $t('dashboard.admin.console.runs.none') }}
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.runs.columns.status')">
          <RunStatus :status="row.status" />
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.console.runs.columns.duration')">
          {{ durationText(Number(row.durationMs), locale) }}
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.console.runs.columns.hostCalls')">
          {{ Number(row.hostCallCount) }}
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.runs.columns.failure')">
          <RunFailureKind :kind="row.failureKind" />
        </TableCell>
        <TableCell class="max-w-xs text-muted-foreground wrap-anywhere" :label="$t('dashboard.admin.console.runs.columns.error')">
          {{ row.errorMessage }}
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
