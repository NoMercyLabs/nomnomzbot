<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The people who use the bot, one row each. The bot sorts the list (newest first), so the heads do not sort.
// The name is the one button of a row: it picks the user, for the keyboard and the pointer alike.
import { useI18n } from 'vue-i18n';

import type { AdminUser } from '@/api/adminUsers';
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
import { useTime } from '@/composables/useTime';

defineProps<{ rows: readonly AdminUser[]; selectedId: string }>();
defineEmits<{ pick: [row: AdminUser] }>();

const { t } = useI18n();
const time = useTime();

function tone(role: string): StatusTone {
  return role === 'admin'
    ? 'live'
    : 'off';
}

function roleLabel(role: string): string {
  return role === 'admin'
    ? t('dashboard.admin.users.role.admin')
    : t('dashboard.admin.users.role.user');
}

function initials(name: string): string {
  return name
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase();
}

function day(value: string | null | undefined): string {
  return value
    ? time.date(value)
    : t('dashboard.admin.users.never');
}
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ t('dashboard.admin.users.columns.user') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.users.columns.role') }}</TableHead>
        <TableHead class="text-right">{{ t('dashboard.admin.users.columns.channels') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.users.columns.lastActive') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.users.columns.created') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow
        v-for="row in rows"
        :key="row.id"
        data-user-row
        :data-state="row.id === selectedId ? 'selected' : undefined"
        class="cursor-pointer"
        @click="$emit('pick', row)"
      >
        <TableCell stack="title">
          <button
            type="button"
            :aria-pressed="row.id === selectedId"
            class="flex min-h-11 items-center gap-3 rounded-md text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
            @click.stop="$emit('pick', row)"
          >
            <Avatar class="size-8 rounded-lg">
              <AvatarFallback class="rounded-lg">{{ initials(row.displayName) }}</AvatarFallback>
            </Avatar>
            <span class="flex min-w-0 flex-col">
              <span class="truncate font-medium">{{ row.displayName }}</span>
              <span class="truncate text-sm text-muted-foreground">{{ row.login }}</span>
            </span>
          </button>
        </TableCell>
        <TableCell :label="t('dashboard.admin.users.columns.role')">
          <StatusBadge :tone="tone(row.role)" :label="roleLabel(row.role)" />
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="t('dashboard.admin.users.columns.channels')">{{ Number(row.channelCount) }}</TableCell>
        <TableCell class="text-muted-foreground" :label="t('dashboard.admin.users.columns.lastActive')">{{ day(row.lastActive) }}</TableCell>
        <TableCell class="text-muted-foreground" :label="t('dashboard.admin.users.columns.created')">{{ day(row.createdAt) }}</TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
