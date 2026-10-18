<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every outbound webhook attempt, newest first. A replay sends a new attempt with the original body;
// it is offered only while the endpoint still exists and is enabled (the bot says so with endpointCanReplay).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePlatformCan } from '@/composables/usePlatformCan';

import type { AdminWebhookDelivery } from '@/api/adminActivity';
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

const { locale, t } = useI18n();
const { can } = usePlatformCan();

function duration(value: AdminWebhookDelivery['durationMs']): string {
  return value == null
    ? t('dashboard.admin.webhookDeliveries.none')
    : t('dashboard.admin.webhookDeliveries.ms', { ms: Number(value) });
}

const stamps = computed<Intl.DateTimeFormat>(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }),
);

function stamp(value: string): string {
  return stamps.value.format(new Date(value));
}
</script>

<template>
  <Table>
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
        <TableCell class="font-medium">{{ row.endpointName }}</TableCell>
        <TableCell class="font-mono text-sm">{{ row.eventType }}</TableCell>
        <TableCell>
          <StatusBadge
            :tone="row.status === 'succeeded'
              ? 'ok'
              : 'bad'"
            :label="row.status"
          />
        </TableCell>
        <TableCell class="text-right tabular-nums">{{ row.responseCode ?? $t('dashboard.admin.webhookDeliveries.none') }}</TableCell>
        <TableCell class="text-right tabular-nums">
          {{ duration(row.durationMs) }}
        </TableCell>
        <TableCell class="text-right tabular-nums">{{ row.attempt }}</TableCell>
        <TableCell class="text-muted-foreground">{{ stamp(row.createdAt) }}</TableCell>
        <TableCell class="text-muted-foreground">{{ row.error ?? $t('dashboard.admin.webhookDeliveries.none') }}</TableCell>
        <TableCell class="text-right">
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
