<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n'

import { useBotStatus, useConnections, type IntegrationStatus } from '@/api/home'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'
import { useProviderName } from '@/composables/useProviderName'

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const platformName = useProviderName()

const connectionsQuery = useConnections(channelId)
const botQuery = useBotStatus(channelId)
const connectionsState = usePanelState(connectionsQuery)

function connectionTone(connection: IntegrationStatus): 'ok' | 'bad' | 'off' {
  if (!connection.connected)
    return 'off'

  return connection.needsReauth
    ? 'bad'
    : 'ok'
}

function connectionLabel(connection: IntegrationStatus): string {
  if (!connection.connected)
    return t('dashboard.home.connections.notConnected')

  return connection.needsReauth
    ? t('dashboard.home.connections.reauth')
    : t('dashboard.home.connections.connected')
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.home.connections.title')"
    :description="t('dashboard.home.connections.services')"
    tier="quiet"
    :state="connectionsState"
    :error-text="t('dashboard.home.connections.error')"
    @retry="connectionsQuery.refetch()"
  >
    <RowGroup>
      <SummaryRow
        :title="t('dashboard.home.connections.bot')"
        :detail="botQuery.data.value?.displayName ?? undefined"
      >
        <template #trailing>
          <StatusBadge
            :tone="botQuery.data.value?.connected ? 'ok' : 'off'"
            :label="
              botQuery.data.value?.connected
                ? t('dashboard.home.connections.connected')
                : t('dashboard.home.connections.botOffline')
            "
          />
        </template>
      </SummaryRow>
      <SummaryRow
        v-for="connection in connectionsQuery.data.value ?? []"
        :key="connection.provider"
        :title="platformName(connection.provider)"
        :detail="connection.accountName ?? undefined"
      >
        <template #trailing>
          <StatusBadge :tone="connectionTone(connection)" :label="connectionLabel(connection)" />
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>
