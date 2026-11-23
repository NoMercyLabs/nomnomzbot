<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Connections tab of a channel console: every platform and integration link of the channel with its
// reauth, decrypt and scope state, from the diagnostics `connections` section. A failed read shows one error
// with a retry.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelDiagnostics, type ChannelConnection } from '@/api/adminDiagnostics';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';

import ChannelConnectionsTable from './ChannelConnectionsTable.vue';

const props = defineProps<{ channelId: string }>();

const { t } = useI18n();

const diagnostics = useChannelDiagnostics(() => props.channelId, ['connections']);

const connections = computed<ChannelConnection[]>(() => diagnostics.data.value?.connections ?? []);

const state = usePanelState(diagnostics, (data) =>
  (data?.connections?.length ?? 0) === 0);
</script>

<template>
  <div data-connections-tab>
    <PanelCard
      :title="t('dashboard.admin.console.connections.title')"
      :description="t('dashboard.admin.console.connections.description')"
      :state="state"
      :empty-text="t('dashboard.admin.console.connections.empty')"
      :error-text="t('dashboard.admin.console.connections.error')"
      @retry="diagnostics.refetch()"
    >
      <ChannelConnectionsTable :connections="connections" />
    </PanelCard>
  </div>
</template>
