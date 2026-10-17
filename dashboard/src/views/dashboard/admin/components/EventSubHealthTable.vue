<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One row per EventSub topic, grouped under its channel. A channel with no topic still shows one row,
// so a missing registry is visible and never padded with a guessed "healthy" entry.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type {
  AdminEventSubTenantHealth,
  AdminEventSubTopicHealth,
} from '@/api/adminActivity';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

type Line = {
  key: string;
  channel: string | null;
  topic: AdminEventSubTopicHealth | null;
};

const props = defineProps<{ rows: readonly AdminEventSubTenantHealth[] }>();

const { locale } = useI18n();

function linesOf(tenant: AdminEventSubTenantHealth): Line[] {
  if (tenant.topics.length === 0)
    return [{
      key: tenant.broadcasterId,
      channel: tenant.channelDisplayName,
      topic: null,
    }];

  return tenant.topics.map((topic: AdminEventSubTopicHealth, index: number): Line => ({
    key: topic.subscriptionId,
    channel: index === 0
      ? tenant.channelDisplayName
      : null,
    topic,
  }));
}

const lines = computed<Line[]>(() => props.rows.flatMap(linesOf));

const dates = computed<Intl.DateTimeFormat>(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }),
);

function day(value: string): string {
  return dates.value.format(new Date(value));
}
</script>

<template>
  <Table>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.channel') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.topic') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.state') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.version') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.confirmed') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.eventsubHealth.columns.error') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="line in lines" :key="line.key" data-topic-row>
        <TableCell class="font-medium">{{ line.channel }}</TableCell>
        <template v-if="line.topic">
          <TableCell class="font-mono text-sm">{{ line.topic.eventType }}</TableCell>
          <TableCell>
            <StatusBadge
              :tone="line.topic.enabled && !line.topic.lastError
                ? 'ok'
                : 'bad'"
              :label="line.topic.status"
            />
          </TableCell>
          <TableCell class="tabular-nums text-muted-foreground">{{ line.topic.version }}</TableCell>
          <TableCell class="text-muted-foreground">{{ day(line.topic.lastConfirmedAt) }}</TableCell>
          <TableCell class="text-muted-foreground">
            {{ line.topic.lastError ?? $t('dashboard.admin.eventsubHealth.noError') }}
          </TableCell>
        </template>
        <TableCell v-else colspan="5" class="text-muted-foreground">
          {{ $t('dashboard.admin.eventsubHealth.noSubscriptions') }}
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
