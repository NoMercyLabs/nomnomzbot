<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The tenants of the registry, one row each: a person who joined, a streamer with a channel or a viewer who
// signed in. The bot sorts the list (newest first), so the heads do not sort. A streamer's name is the one
// link of the row (the channel console); a viewer has no console, so the name is the one button (the detail).
// A click anywhere else in the row does the same. A viewer leaves the channel cells empty.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';

import type { AdminTenant } from '@/api/admin';
import { useBillingTiers } from '@/api/adminBilling';
import { tierNameOf } from '@/api/billing';
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
import { usePlatformCan } from '@/composables/usePlatformCan';

import ActAsDialog, { type ActAsChannel } from './ActAsDialog.vue';
import { statusTone } from './channelDisplay';
import TenantIdentity from './TenantIdentity.vue';
import { platformLine } from './tenantDisplay';

defineProps<{ rows: readonly AdminTenant[]; selectedId: string }>();
defineEmits<{ open: [row: AdminTenant] }>();

const { t } = useI18n();
const { can } = usePlatformCan();
const tiers = useBillingTiers();

// Acting as someone opens a support session first, so it takes both keys.
const canActAs = computed<boolean>(() => can('tenant:access') && can('user:impersonate'));
const actingOn = ref<ActAsChannel | null>(null);

const none: string = '-';

function statusLabel(status: string): string {
  return t(`dashboard.admin.status.${status}`);
}

// A plan shows by its name, never its key; a plan the list does not know shows a dash.
function tierName(key: string): string {
  return tierNameOf(tiers.data.value, key) ?? none;
}

function platforms(row: AdminTenant): string {
  return platformLine(row.platforms, t);
}

// Only a streamer has a channel to act in.
function startActAs({ channel }: AdminTenant): void {
  if (!channel) return;

  actingOn.value = { id: channel.broadcasterId, name: channel.name };
}
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ t('dashboard.admin.tenants.columns.person') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.kind') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.platforms') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.status') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.live') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.tier') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.lastActive') }}</TableHead>
        <TableHead>{{ t('dashboard.admin.tenants.columns.created') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow
        v-for="row in rows"
        :key="row.userId"
        data-tenant-row
        :data-state="row.userId === selectedId ? 'selected' : undefined"
        class="cursor-pointer"
        @click="$emit('open', row)"
      >
        <!-- Act as sits in the name cell, so a last column never scrolls out of view on a narrow screen. -->
        <TableCell stack="title">
          <div class="flex items-center justify-between gap-2">
            <RouterLink
              v-if="row.channel"
              :to="{ name: 'dashboard-admin-channels-console', params: { channelId: row.channel.broadcasterId } }"
              class="flex min-h-11 min-w-0 items-center gap-3 rounded-md text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
              @click.stop
            >
              <TenantIdentity :display-name="row.displayName" :login="row.login" />
            </RouterLink>
            <button
              v-else
              type="button"
              :aria-pressed="row.userId === selectedId"
              class="flex min-h-11 min-w-0 items-center gap-3 rounded-md text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
              @click.stop="$emit('open', row)"
            >
              <TenantIdentity :display-name="row.displayName" :login="row.login" />
            </button>
            <Button
              v-if="canActAs && row.channel"
              variant="ghost"
              size="sm"
              data-act-as
              :aria-label="t('dashboard.admin.actAs.openFor', { channel: row.channel.name })"
              @click.stop="startActAs(row)"
            >
              {{ t('dashboard.admin.actAs.open') }}
            </Button>
          </div>
        </TableCell>
        <TableCell :label="t('dashboard.admin.tenants.columns.kind')">
          {{ t(`dashboard.admin.tenants.kind.${row.kind}`) }}
        </TableCell>
        <TableCell :label="t('dashboard.admin.tenants.columns.platforms')">{{ platforms(row) }}</TableCell>
        <TableCell :label="t('dashboard.admin.tenants.columns.status')">
          <StatusBadge
            v-if="row.channel"
            :tone="statusTone(row.channel.status)"
            :label="statusLabel(row.channel.status)"
          />
          <span v-else class="text-muted-foreground">{{ none }}</span>
        </TableCell>
        <TableCell :label="t('dashboard.admin.tenants.columns.live')">
          <template v-if="row.channel">
            <StatusBadge v-if="row.channel.isLive" tone="live" :label="t('dashboard.admin.live.live')" />
            <span v-else class="text-muted-foreground">{{ t('dashboard.admin.live.offline') }}</span>
          </template>
          <span v-else class="text-muted-foreground">{{ none }}</span>
        </TableCell>
        <TableCell :label="t('dashboard.admin.tenants.columns.tier')">
          <template v-if="row.channel">{{ tierName(row.channel.billingTierKey) }}</template>
          <span v-else class="text-muted-foreground">{{ none }}</span>
        </TableCell>
        <TableCell class="text-muted-foreground" :label="t('dashboard.admin.tenants.columns.lastActive')">
          <DateTime v-if="row.lastActive" :value="row.lastActive" variant="date" />
          <template v-else>{{ t('dashboard.admin.tenants.never') }}</template>
        </TableCell>
        <TableCell class="text-muted-foreground" :label="t('dashboard.admin.tenants.columns.created')">
          <DateTime :value="row.createdAt" variant="date" />
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
  <ActAsDialog v-if="canActAs && actingOn !== null" :channel="actingOn" @close="actingOn = null" />
</template>
