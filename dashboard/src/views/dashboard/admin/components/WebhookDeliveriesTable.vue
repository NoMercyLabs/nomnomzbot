<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every outbound webhook attempt, newest first. A replay sends a new attempt with the original body;
// it is offered only while the endpoint still exists and is enabled (the bot says so with endpointCanReplay).
import { useI18n } from 'vue-i18n';

import { usePlatformCan } from '@/composables/usePlatformCan';

import type { AdminWebhookDelivery } from '@/api/adminActivity';
import DateTime from '@/components/layout/DateTime.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ rows: readonly AdminWebhookDelivery[]; replaying: boolean }>();

defineEmits<{ replay: [id: number] }>();

const { t } = useI18n();
const { can } = usePlatformCan();

function duration(value: AdminWebhookDelivery['durationMs']): string {
  return value == null
    ? t('dashboard.admin.webhookDeliveries.none')
    : t('dashboard.admin.webhookDeliveries.ms', { ms: Number(value) });
}

</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.admin.webhookDeliveries.columns.endpoint') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.webhookDeliveries.columns.event') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.webhookDeliveries.columns.status') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.webhookDeliveries.columns.code') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.webhookDeliveries.columns.duration') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.webhookDeliveries.columns.attempt') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.webhookDeliveries.columns.sent') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.webhookDeliveries.columns.error') }}</TableHead>
        <TableHead />
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="row in rows" :key="String(row.id)" data-delivery-row>
        <TableCell stack="title" class="font-medium">{{ row.endpointName }}</TableCell>
        <TableCell class="font-mono text-sm" :label="$t('dashboard.admin.webhookDeliveries.columns.event')">{{ row.eventType }}</TableCell>
        <TableCell :label="$t('dashboard.admin.webhookDeliveries.columns.status')">
          <StatusBadge
            :tone="row.status === 'succeeded'
              ? 'ok'
              : 'bad'"
            :label="row.status"
          />
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.webhookDeliveries.columns.code')">{{ row.responseCode ?? $t('dashboard.admin.webhookDeliveries.none') }}</TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.webhookDeliveries.columns.duration')">
          {{ duration(row.durationMs) }}
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.webhookDeliveries.columns.attempt')">{{ row.attempt }}</TableCell>
        <TableCell class="text-muted-foreground" :label="$t('dashboard.admin.webhookDeliveries.columns.sent')"><DateTime :value="row.createdAt" variant="dateTime" /></TableCell>
        <TableCell class="text-muted-foreground" :label="$t('dashboard.admin.webhookDeliveries.columns.error')">{{ row.error ?? $t('dashboard.admin.webhookDeliveries.none') }}</TableCell>
        <TableCell stack="actions" class="text-right">
          <Button
            v-if="row.endpointCanReplay && can('iam:manage')"
            variant="outline"
            size="sm"
            :disabled="replaying"
            @click="$emit('replay', Number(row.id))"
          >
            {{ $t('dashboard.admin.webhookDeliveries.replay') }}
          </Button>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
