<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// StatusBadge in its five tones and ConnectionRow in its three states.
import { useI18n } from 'vue-i18n';

import ConnectionRow, { type ConnectionState } from '@/components/layout/ConnectionRow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';

const { t } = useI18n();

const TONES = ['ok', 'bad', 'live', 'off', 'attention'] as const satisfies readonly StatusTone[];

const CONNECTIONS = [
  { state: 'connected', provider: 'twitch' },
  { state: 'disconnected', provider: 'spotify' },
  { state: 'attention', provider: 'discord' },
] as const satisfies readonly { state: ConnectionState; provider: string }[];
</script>

<template>
  <section data-catalog="connections" class="grid gap-4">
    <div data-catalog-badges class="flex flex-wrap gap-4">
      <StatusBadge
        v-for="tone in TONES"
        :key="tone"
        :tone="tone"
        :label="t(`dashboard.catalog.connections.tone.${tone}`)"
      />
    </div>
    <PanelCard :title="t('dashboard.catalog.connections.title')">
      <RowGroup>
        <ConnectionRow
          v-for="row in CONNECTIONS"
          :key="row.state"
          :provider="row.provider"
          :name="t(`dashboard.catalog.connections.name.${row.state}`)"
          :state="row.state"
          :account="row.state === 'connected' ? t('dashboard.catalog.connections.account') : undefined"
          :detail="t(`dashboard.catalog.connections.detail.${row.state}`)"
        >
          <template #actions>
            <!-- One primary per group: the connect action is outline because no row is the page's main task. -->
            <Button variant="outline" size="sm">{{ t(`dashboard.catalog.connections.action.${row.state}`) }}</Button>
          </template>
        </ConnectionRow>
      </RowGroup>
    </PanelCard>
  </section>
</template>
