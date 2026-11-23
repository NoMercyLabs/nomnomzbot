<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The connections of one channel. A connection that works reads as plain muted words. A connection that needs
// action carries a dot, an icon and words: the warning tone for what the streamer can fix (sign in again,
// grant a missing scope), the red tone only for a token the bot cannot decrypt. The row's own id is only a key.
import { useI18n } from 'vue-i18n';

import AlertTriangle from '~icons/nnz/alert-triangle';

import type { ChannelConnection } from '@/api/adminDiagnostics';
import DateTime from '@/components/layout/DateTime.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

defineProps<{ connections: readonly ChannelConnection[] }>();

const { t, te } = useI18n();

type ConnectionState = { key: string; tone: StatusTone | null };

const ICON_TONES = {
  bad: 'text-destructive',
  attention: 'text-warning',
  ok: 'text-success',
  live: 'text-destructive',
  off: 'text-muted-foreground',
} as const satisfies Record<StatusTone, string>;

// The worst problem wins: a token that cannot be read, then a sign-in to redo, then a missing scope.
function stateOf(connection: ChannelConnection): ConnectionState {
  if (connection.decryptFailed)
    return { key: 'dashboard.admin.console.connections.state.decryptFailed', tone: 'bad' };

  if (connection.needsReauth)
    return { key: 'dashboard.admin.console.connections.state.needsReauth', tone: 'attention' };

  if (connection.missingScopes?.length)
    return { key: 'dashboard.admin.console.connections.state.missingScopes', tone: 'attention' };

  if (!connection.connected)
    return { key: 'common.connection.disconnected', tone: 'off' };

  return { key: 'common.connection.connected', tone: null };
}

// A family the bot adds later shows as the bot names it.
function kindText(kind: string): string {
  const key = `dashboard.admin.console.connections.kind.${kind}`;

  return te(key)
    ? t(key)
    : kind;
}
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.provider') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.state') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.scopes') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.token') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.activity') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.connections.columns.problem') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="connection in connections" :key="connection.id" data-connection-row>
        <TableCell stack="title">
          <div class="font-medium">{{ connection.provider }}</div>
          <div class="text-sm text-muted-foreground">{{ kindText(connection.kind) }}</div>
          <div v-if="connection.accountName || connection.accountId" class="text-sm wrap-anywhere">
            {{
              connection.accountName && connection.accountId
                ? t('dashboard.admin.console.connections.account', { name: connection.accountName, id: connection.accountId })
                : connection.accountName ?? connection.accountId
            }}
          </div>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.connections.columns.state')">
          <div class="flex items-center gap-2">
            <template v-if="stateOf(connection).tone">
              <AlertTriangle
                data-connection-icon
                aria-hidden="true"
                class="size-4 flex-none"
                :class="ICON_TONES[stateOf(connection).tone!]"
              />
              <StatusBadge
                :tone="stateOf(connection).tone!"
                :label="t(stateOf(connection).key)"
              />
            </template>
            <span v-else class="text-sm text-muted-foreground">
              {{ t(stateOf(connection).key) }}
            </span>
          </div>
          <div v-if="connection.status" class="text-sm text-muted-foreground">
            {{ t('dashboard.admin.console.connections.stored', { status: connection.status }) }}
          </div>
        </TableCell>
        <TableCell class="text-sm" :label="$t('dashboard.admin.console.connections.columns.scopes')">
          <div v-if="connection.missingScopes?.length" class="font-mono wrap-anywhere">
            {{ t('dashboard.admin.console.connections.missing', { scopes: connection.missingScopes.join(', ') }) }}
          </div>
          <div v-if="connection.grantedScopes?.length" class="font-mono text-muted-foreground wrap-anywhere">
            {{ t('dashboard.admin.console.connections.granted', { scopes: connection.grantedScopes.join(', ') }) }}
          </div>
          <span v-if="!connection.missingScopes?.length && !connection.grantedScopes?.length">
            {{ t('dashboard.admin.console.connections.none') }}
          </span>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.connections.columns.token')">
          <DateTime v-if="connection.tokenExpiresAt" :value="connection.tokenExpiresAt" variant="dateTime" />
          <span v-else>{{ t('dashboard.admin.console.connections.none') }}</span>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.connections.columns.activity')">
          <DateTime v-if="connection.lastActivityAt" :value="connection.lastActivityAt" variant="dateTime" />
          <span v-else>{{ t('dashboard.admin.console.connections.none') }}</span>
        </TableCell>
        <TableCell class="text-sm" :label="$t('dashboard.admin.console.connections.columns.problem')">
          <template v-if="connection.lastError || Number(connection.consecutiveFailures ?? 0) > 0">
            <div v-if="connection.lastError" class="wrap-anywhere">{{ connection.lastError }}</div>
            <DateTime v-if="connection.lastErrorAt" :value="connection.lastErrorAt" variant="dateTime" />
            <div v-if="Number(connection.consecutiveFailures ?? 0) > 0" class="text-muted-foreground">
              {{ t('dashboard.admin.console.connections.failures', { count: Number(connection.consecutiveFailures) }) }}
            </div>
          </template>
          <span v-else>{{ t('dashboard.admin.console.connections.none') }}</span>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
