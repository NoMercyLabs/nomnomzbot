<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The background job queue, newest first. A retry queues a brand-new run of a failed job;
// it is offered only when the bot says so with canRetry (failed, and the pipeline still exists).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminScheduledJob } from '@/api/adminActivity';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ rows: readonly AdminScheduledJob[]; retrying: boolean }>();

defineEmits<{ retry: [id: string] }>();

const { locale, t } = useI18n();

const tones: Record<string, StatusTone> = {
  succeeded: 'ok',
  failed: 'bad',
  running: 'live',
};

function toneOf(state: string): StatusTone {
  return tones[state] ?? 'off';
}

const stamps = computed<Intl.DateTimeFormat>(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }),
);

function stamp(value: string | null | undefined): string {
  return value == null
    ? t('dashboard.admin.scheduledJobs.none')
    : stamps.value.format(new Date(value));
}
</script>

<template>
  <Table>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.channel") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.pipeline") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.state") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.due") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.fired") }}</TableHead>
        <TableHead>{{ $t("dashboard.admin.scheduledJobs.columns.trigger") }}</TableHead>
        <TableHead />
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="row in rows" :key="row.id" data-job-row>
        <TableCell class="font-medium">{{ row.channelDisplayName }}</TableCell>
        <TableCell :class="{ 'text-muted-foreground': !row.pipelineExists }">
          {{ row.pipelineName ?? $t("dashboard.admin.scheduledJobs.pipelineGone") }}
        </TableCell>
        <TableCell>
          <StatusBadge :tone="toneOf(row.displayState)" :label="row.displayState" />
        </TableCell>
        <TableCell class="text-muted-foreground">{{ stamp(row.dueAt) }}</TableCell>
        <TableCell class="text-muted-foreground">{{ stamp(row.firedAt) }}</TableCell>
        <TableCell>{{ row.triggeredByDisplayName }}</TableCell>
        <TableCell class="text-right">
          <Button
            v-if="row.canRetry"
            variant="outline"
            size="sm"
            :disabled="retrying"
            @click="$emit('retry', row.id)"
          >
            {{ $t("dashboard.admin.scheduledJobs.retry") }}
          </Button>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
