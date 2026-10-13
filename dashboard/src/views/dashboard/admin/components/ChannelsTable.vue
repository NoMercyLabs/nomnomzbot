<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channels of the registry, one row each. The bot sorts the list (newest first), so the heads do not sort.
// The name is the one button of a row: it picks the channel, for the keyboard and the pointer alike.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenant } from '@/api/admin';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Avatar, AvatarFallback } from '@/components/ui/avatar';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ rows: readonly AdminTenant[]; selectedId: string }>();
defineEmits<{ pick: [row: AdminTenant] }>();

const { t, locale } = useI18n();

const TONES: Readonly<Record<string, StatusTone>> = {
  active: 'ok',
  suspended: 'off',
  churned: 'off',
  platform_banned: 'bad',
};

function tone(status: string | undefined): StatusTone {
  return TONES[status ?? ''] ?? 'off';
}

function statusLabel(status: string | undefined): string {
  return t(`dashboard.admin.status.${status ?? 'unknown'}`);
}

function initials(name: string | undefined): string {
  return (name ?? '')
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase();
}

const dates = computed<Intl.DateTimeFormat>(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }),
);

function created(value: string | undefined): string {
  return value
    ? dates.value.format(new Date(value))
    : '';
}
</script>

<template>
  <Table>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ t('dashboard.admin.columns.channel') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.columns.status') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.columns.live') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.columns.created') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow
        v-for="row in rows"
        :key="row.id"
        data-channel-row
        :data-state="row.id === selectedId ? 'selected' : undefined"
        class="cursor-pointer"
        @click="$emit('pick', row)"
      >
        <TableCell>
          <button
            type="button"
            :aria-pressed="row.id === selectedId"
            class="flex min-h-11 items-center gap-3 rounded-md text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
            @click.stop="$emit('pick', row)"
          >
            <Avatar class="size-8 rounded-lg">
              <AvatarFallback class="rounded-lg">{{ initials(row.name) }}</AvatarFallback>
            </Avatar>
            <span class="font-medium">{{ row.name }}</span>
          </button>
        </TableCell>
        <TableCell>
          <StatusBadge :tone="tone(row.status)" :label="statusLabel(row.status)" />
        </TableCell>
        <TableCell>
          <StatusBadge v-if="row.isLive" tone="live" :label="t('dashboard.admin.live.live')" />
          <span v-else class="text-muted-foreground">{{ t('dashboard.admin.live.offline') }}</span>
        </TableCell>
        <TableCell class="text-muted-foreground">{{ created(row.createdAt) }}</TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
