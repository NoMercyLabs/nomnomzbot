<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Where the stream info goes: Twitch is always there, Kick and YouTube connect from here, X Live is not built yet.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useConnections, type IntegrationStatus } from '@/api/home';
import { useConnectIntegration } from '@/api/stream';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

// The scope set each platform needs to manage a stream (server OAuthProviderRegistry).
const CONNECTABLE: readonly { provider: string; scopeSetKey: string }[] = [
  { provider: 'kick', scopeSetKey: 'kick.chat' },
  { provider: 'youtube', scopeSetKey: 'youtube.manage' },
];

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();
const connectionsQuery = useConnections(channelId);
const connect = useConnectIntegration(channelId);
const state = usePanelState(connectionsQuery);

function connectionOf(provider: string): IntegrationStatus | undefined {
  return (connectionsQuery.data.value ?? []).find(
    (connection: IntegrationStatus) => connection.provider === provider,
  );
}

function platformName(provider: string): string {
  return t(`common.provider.${provider}`);
}

const twitchAccount = computed(() => current.value?.displayName);

async function open(provider: string, scopeSetKey: string): Promise<void> {
  const url: string = await connect.mutateAsync({ provider, scopeSetKey });

  window.location.assign(url);
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.stream.platforms.title')"
    :description="t('dashboard.stream.platforms.description')"
    tier="quiet"
    :state="state"
    :error-text="t('dashboard.stream.platforms.error')"
    @retry="connectionsQuery.refetch()"
  >
    <RowGroup>
      <SummaryRow :title="platformName('twitch')" :detail="twitchAccount" data-platform="twitch">
        <template #trailing>
          <StatusBadge tone="ok" :label="t('dashboard.stream.platforms.connected')" />
        </template>
      </SummaryRow>
      <div v-for="target in CONNECTABLE" :key="target.provider" :data-platform="target.provider">
        <SummaryRow
          :title="platformName(target.provider)"
          :detail="connectionOf(target.provider)?.accountName ?? undefined"
        >
          <template #trailing>
            <StatusBadge
              v-if="connectionOf(target.provider)?.connected"
              tone="ok"
              :label="t('dashboard.stream.platforms.connected')"
            />
            <div v-else class="flex items-center gap-3">
              <StatusBadge tone="off" :label="t('dashboard.stream.platforms.notConnected')" />
              <InlineFlow
                kind="confirm"
                right
                :label="
                  t('dashboard.stream.platforms.connectLabel', {
                    platform: platformName(target.provider),
                  })
                "
                :ask="
                  t('dashboard.stream.platforms.connectAsk', {
                    platform: platformName(target.provider),
                  })
                "
                :yes="t('dashboard.stream.platforms.connect')"
                :action="() => open(target.provider, target.scopeSetKey)"
              >
                <Button variant="outline" size="sm">{{
                  t('dashboard.stream.platforms.connect')
                }}</Button>
              </InlineFlow>
            </div>
          </template>
        </SummaryRow>
      </div>
      <SummaryRow
        :title="t('dashboard.stream.platforms.xlive')"
        :detail="
          t('dashboard.stream.platforms.unavailableWhy', {
            platform: t('dashboard.stream.platforms.xlive'),
          })
        "
        data-platform="xlive"
      >
        <template #trailing>
          <StatusBadge tone="off" :label="t('dashboard.stream.platforms.unavailable')" />
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>
