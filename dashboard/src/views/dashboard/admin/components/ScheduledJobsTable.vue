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
import { useI18n } from 'vue-i18n';

import { usePlatformCan } from '@/composables/usePlatformCan';
import { useTime } from '@/composables/useTime';

import type { AdminScheduledJob } from '@/api/adminActivity';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { lookup } from '@/lib/array';
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

const { t } = useI18n();
const time = useTime();
const { can } = usePlatformCan();

const tones = {
  succeeded: 'ok',
  failed: 'bad',
  running: 'live',
} as const satisfies Record<string, StatusTone>;

function toneOf(state: string): StatusTone {
  return lookup(tones, state) ?? 'off';
}

function stamp(value: string | null | undefined): string {
  return value == null
    ? t('dashboard.admin.scheduledJobs.none')
    : time.dateTime(value);
}
</script>

<template>
  <Table stacked>
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
        <TableCell stack="title" class="font-medium">{{ row.channelDisplayName }}</TableCell>
        <TableCell :class="{ 'text-muted-foreground': !row.pipelineExists }" :label="$t('dashboard.admin.scheduledJobs.columns.pipeline')">
          {{ row.pipelineName ?? $t("dashboard.admin.scheduledJobs.pipelineGone") }}
        </TableCell>
        <TableCell :label="$t('dashboard.admin.scheduledJobs.columns.state')">
          <StatusBadge :tone="toneOf(row.displayState)" :label="row.displayState" />
        </TableCell>
        <TableCell class="text-muted-foreground" :label="$t('dashboard.admin.scheduledJobs.columns.due')">{{ stamp(row.dueAt) }}</TableCell>
        <TableCell class="text-muted-foreground" :label="$t('dashboard.admin.scheduledJobs.columns.fired')">{{ stamp(row.firedAt) }}</TableCell>
        <TableCell :label="$t('dashboard.admin.scheduledJobs.columns.trigger')">{{ row.triggeredByDisplayName }}</TableCell>
        <TableCell stack="actions" class="text-right">
          <Button
            v-if="row.canRetry && can('iam:manage')"
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
