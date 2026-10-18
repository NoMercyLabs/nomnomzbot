<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The keys of this channel, one row each: name, the start of the secret, what it may do, when it was last
// used and when it ends. A revoked key stays listed (the audit trail) and has no actions.
import Rotate from '~icons/nnz/rotate';
import Trash from '~icons/nnz/trash';

import type { AutomationToken } from '@/api/automationTokens';
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
import { useCan } from '@/composables/useCan';
import { useTime } from '@/composables/useTime';

const { can } = useCan();

defineProps<{ tokens: readonly AutomationToken[] }>();
defineEmits<{ rotate: [token: AutomationToken]; revoke: [token: AutomationToken] }>();

const time = useTime();
const day = (value: string | null | undefined): string =>
  time.date(value);

function toneOf(token: AutomationToken): StatusTone {
  if (token.revokedAt !== null && token.revokedAt !== undefined)
    return 'off';

  const ends = token.expiresAt;

  return ends !== null && ends !== undefined && new Date(ends).getTime() < Date.now()
    ? 'bad'
    : 'ok';
}

function stateOf(token: AutomationToken): string {
  const tone = toneOf(token);

  if (tone === 'off')
    return 'revoked';

  return tone === 'bad'
    ? 'expired'
    : 'active';
}
</script>

<template>
  <Table>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.settings.apiDevices.columns.name') }}</TableHead>
        <TableHead>{{ $t('dashboard.settings.apiDevices.columns.scopes') }}</TableHead>
        <TableHead>{{ $t('dashboard.settings.apiDevices.columns.lastUsed') }}</TableHead>
        <TableHead>{{ $t('dashboard.settings.apiDevices.columns.expires') }}</TableHead>
        <TableHead>{{ $t('dashboard.settings.apiDevices.columns.state') }}</TableHead>
        <TableHead v-if="can('automation:tokens:write')" class="w-24" />
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="token in tokens" :key="token.id" data-token-row :data-state="stateOf(token)">
        <TableCell>
          <span class="block font-medium">{{ token.name }}</span>
          <span class="font-mono text-sm text-muted-foreground">{{ $t('dashboard.settings.apiDevices.prefixHint', { prefix: token.tokenPrefix }) }}</span>
        </TableCell>
        <TableCell class="text-sm">
          {{ token.scopes.join(', ') }}
          <span v-if="token.allowedPipelineIds.length > 0" class="block text-muted-foreground">
            {{ $t('dashboard.settings.apiDevices.pinned', { count: token.allowedPipelineIds.length }) }}
          </span>
        </TableCell>
        <TableCell class="text-muted-foreground">
          {{ day(token.lastUsedAt) || $t('dashboard.settings.apiDevices.never') }}
        </TableCell>
        <TableCell class="text-muted-foreground">
          {{ day(token.expiresAt) || $t('dashboard.settings.apiDevices.noExpiry') }}
        </TableCell>
        <TableCell>
          <StatusBadge
            :tone="toneOf(token)"
            :label="$t(`dashboard.settings.apiDevices.state.${stateOf(token)}`)"
          />
        </TableCell>
        <TableCell v-if="can('automation:tokens:write')">
          <div v-if="stateOf(token) !== 'revoked'" class="flex justify-end gap-1">
            <Button
              data-rotate
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.settings.apiDevices.rotate.aria', { name: token.name })"
              @click="$emit('rotate', token)"
            >
              <Rotate class="size-5" />
            </Button>
            <Button
              data-revoke
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.settings.apiDevices.revoke.aria', { name: token.name })"
              @click="$emit('revoke', token)"
            >
              <Trash class="size-5" />
            </Button>
          </div>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
