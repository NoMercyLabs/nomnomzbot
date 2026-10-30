<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channels of the registry, one row each. The bot sorts the list (newest first), so the heads do not sort.
// The name is the one button of a row: it picks the channel, for the keyboard and the pointer alike.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import type { AdminTenant } from '@/api/admin';
import { Button } from '@/components/ui/button';
import { usePlatformCan } from '@/composables/usePlatformCan';

import ActAsDialog, { type ActAsChannel } from './ActAsDialog.vue';
import DateTime from '@/components/layout/DateTime.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { lookup } from '@/lib/array';
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

const { t } = useI18n();
const { can } = usePlatformCan();

// Acting as someone opens a support session first, so it takes both keys.
const canActAs = computed<boolean>(() => can('tenant:access') && can('user:impersonate'));
const actingOn = ref<ActAsChannel | null>(null);

const TONES = {
  active: 'ok',
  suspended: 'off',
  churned: 'off',
  platform_banned: 'bad',
} as const satisfies Readonly<Record<string, StatusTone>>;

function tone(status: string | undefined): StatusTone {
  return lookup(TONES, status) ?? 'off';
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
        <!-- Act as sits in the name cell: the list pane is narrow, and a last column would scroll out of view. -->
        <TableCell>
          <div class="flex items-center justify-between gap-2">
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
            <Button
              v-if="canActAs"
              variant="ghost"
              size="sm"
              data-act-as
              :aria-label="t('dashboard.admin.actAs.openFor', { channel: row.name })"
              @click.stop="actingOn = { id: row.id, name: row.name }"
            >
              {{ t('dashboard.admin.actAs.open') }}
            </Button>
          </div>
        </TableCell>
        <TableCell>
          <StatusBadge :tone="tone(row.status)" :label="statusLabel(row.status)" />
        </TableCell>
        <TableCell>
          <StatusBadge v-if="row.isLive" tone="live" :label="t('dashboard.admin.live.live')" />
          <span v-else class="text-muted-foreground">{{ t('dashboard.admin.live.offline') }}</span>
        </TableCell>
        <TableCell class="text-muted-foreground"><DateTime :value="row.createdAt" variant="date" /></TableCell>
      </TableRow>
    </TableBody>
  </Table>
  <ActAsDialog v-if="canActAs && actingOn !== null" :channel="actingOn" @close="actingOn = null" />
</template>
