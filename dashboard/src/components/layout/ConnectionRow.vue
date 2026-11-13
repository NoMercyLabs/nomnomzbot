<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One connection of an account, drawn one way everywhere: the logo, the name and the connected account, a status
// dot with words, a detail line, and the buttons. It only shows. The page owns the sign-in, the mutations and the
// confirm dialog, and puts its buttons in the actions slot: one solid button per row (Connect or Reconnect when it
// is the one thing to do), Disconnect as a ghost or outline button.
import PlatformLogo from './PlatformLogo.vue';
import StatusBadge, { type StatusTone } from './StatusBadge.vue';
import SummaryRow from './SummaryRow.vue';

export type ConnectionState = 'connected' | 'disconnected' | 'attention';

const tones = {
  connected: 'ok',
  disconnected: 'off',
  attention: 'attention',
} as const satisfies Record<ConnectionState, StatusTone>;

const labelKeys = {
  connected: 'common.connection.connected',
  disconnected: 'common.connection.disconnected',
  attention: 'common.connection.attention',
} as const satisfies Record<ConnectionState, string>;

// label: a more exact reason than the state's own words, such as "Sign in again" for an attention row.
defineProps<{
  provider?: string;
  name: string;
  state: ConnectionState;
  account?: string;
  detail?: string;
  label?: string;
}>();
</script>

<template>
  <div data-slot="connection-row" :data-state="state">
    <SummaryRow wrap :title="name" :detail="detail">
      <template #leading>
        <!-- The box keeps its width when a provider has no logo, so every name in a list starts at the same edge. -->
        <slot name="leading">
          <span data-slot="connection-logo" class="size-5 shrink-0">
            <PlatformLogo v-if="provider" :platform="provider" class="size-5" />
          </span>
        </slot>
      </template>
      <template #title>
        <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span class="wrap-break-word">{{ name }}</span>
          <StatusBadge :tone="tones[state]" :label="label ?? $t(labelKeys[state])" />
          <span v-if="account" data-slot="connection-account" class="text-sm font-normal text-muted-foreground wrap-break-word">
            {{ $t('common.connection.as', { account }) }}
          </span>
        </span>
      </template>
      <template v-if="$slots.actions" #trailing>
        <slot name="actions" />
      </template>
    </SummaryRow>
    <!-- pl-11 = the row padding (12) + the logo box (20) + the gap (12): the lines start under the name. -->
    <div v-if="$slots.credential" data-slot="connection-credential" class="pr-3 pb-2 pl-11">
      <slot name="credential" />
    </div>
    <div v-if="$slots.detail" data-slot="connection-detail" class="pr-3 pb-2 pl-11">
      <slot name="detail" />
    </div>
  </div>
</template>
