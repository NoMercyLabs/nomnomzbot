<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Two groups as in the mockup: the streaming platforms, then the bot account and the services.
// Only Kick and YouTube have a web connect flow in the API (the same one the Stream page uses);
// Twitch is where the channel signed in, and a service without a flow shows its status only.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useBotStatus, useConnections, useHomeStats, type IntegrationStatus } from '@/api/home';
import { useConnectIntegration } from '@/api/stream';
import { platformScopeSets, streamPlatforms } from '@/api/vocabulary';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useConnectWindow } from '@/composables/useConnectWindow';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useProviderName } from '@/composables/useProviderName';

const dotClass = {
  ok: 'bg-success',
  bad: 'bg-destructive',
  live: 'bg-destructive',
  off: 'bg-muted-foreground',
} as const satisfies Record<StatusTone, string>;

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();
const { can } = useCan();
const platformName = useProviderName();

const connectionsQuery = useConnections(channelId);
const botQuery = useBotStatus(channelId);
const statsQuery = useHomeStats(channelId);
const connect = useConnectIntegration(channelId);
const connectWindow = useConnectWindow(channelId);
const connectionsState = usePanelState(connectionsQuery);

const allConnections = computed(() => connectionsQuery.data.value ?? []);
const livePlatforms = computed(() => statsQuery.data.value?.platformsLive ?? []);
const services = computed(() =>
  allConnections.value.filter(
    (connection) => !(streamPlatforms as readonly string[]).includes(connection.provider),
  ),
);

function connectionOf(provider: string): IntegrationStatus | undefined {
  return allConnections.value.find((connection) => connection.provider === provider);
}

function isConnected(provider: string): boolean {
  // The channel signed in with Twitch, so Twitch counts as connected until the list says otherwise.
  return connectionOf(provider)?.connected ?? provider === 'twitch';
}

function isLiveOn(provider: string): boolean {
  return isConnected(provider) && livePlatforms.value.includes(provider);
}

function platformTone(provider: string): StatusTone {
  return isLiveOn(provider)
    ? 'ok'
    : 'off';
}

function accountOf(provider: string): string {
  const named = connectionOf(provider)?.accountName;

  return named ?? current.value?.displayName ?? '';
}

function platformLine(provider: string): string {
  if (!isConnected(provider))
    return t('dashboard.home.connections.notConnected');

  const account = accountOf(provider);

  // Until the stream stats arrive the line names the account and claims neither live nor offline.
  if (!statsQuery.data.value)
    return account;

  return isLiveOn(provider)
    ? t('dashboard.home.connections.platformLive', { account })
    : t('dashboard.home.connections.platformOffline', { account });
}

function scopeSetOf(provider: string): string | undefined {
  return (platformScopeSets as Record<string, string>)[provider];
}

async function open(provider: string): Promise<void> {
  const scopeSetKey = scopeSetOf(provider) as string;
  const url: string = await connect.mutateAsync({ provider, scopeSetKey });

  connectWindow(url);
}

function connectionTone(connection: IntegrationStatus): 'ok' | 'bad' | 'off' {
  if (!connection.connected)
    return 'off';

  return connection.needsReauth
    ? 'bad'
    : 'ok';
}

function connectionLabel(connection: IntegrationStatus): string {
  if (!connection.connected)
    return t('dashboard.home.connections.notConnected');

  return connection.needsReauth
    ? t('dashboard.home.connections.reauth')
    : t('dashboard.home.connections.connected');
}
</script>

<template>
  <PanelCard
    :title="t('dashboard.home.connections.title')"
    tier="quiet"
    :state="connectionsState"
    :error-text="t('dashboard.home.connections.error')"
    @retry="connectionsQuery.refetch()"
  >
    <div class="flex flex-col gap-4">
      <RowGroup data-connections-group="platforms">
        <div v-for="provider in streamPlatforms" :key="provider" :data-connection="provider">
          <SummaryRow truncate :title="platformName(provider)" :detail="platformLine(provider)">
            <template #leading>
              <span
                aria-hidden="true"
                class="size-2 flex-none rounded-full"
                :class="dotClass[platformTone(provider)]"
              />
            </template>
            <template
              v-if="can('integration:write') && !isConnected(provider) && scopeSetOf(provider)"
              #trailing
            >
              <InlineFlow
                kind="confirm"
                right
                :label="
                  t('dashboard.home.connections.connectLabel', { platform: platformName(provider) })
                "
                :ask="
                  t('dashboard.home.connections.connectAsk', { platform: platformName(provider) })
                "
                :yes="t('dashboard.home.connections.connect')"
                :action="() => open(provider)"
              >
                <Button variant="outline" size="sm">{{
                  t('dashboard.home.connections.connect')
                }}</Button>
              </InlineFlow>
            </template>
          </SummaryRow>
        </div>
      </RowGroup>

      <div class="flex flex-col gap-2">
        <h3 class="px-1 text-sm font-medium text-muted-foreground">
          {{ t('dashboard.home.connections.services') }}
        </h3>
        <RowGroup data-connections-group="services">
          <div data-connection="bot">
            <SummaryRow
              truncate
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
          </div>
          <div
            v-for="connection in services"
            :key="connection.provider"
            :data-connection="connection.provider"
          >
            <SummaryRow
              truncate
              :title="platformName(connection.provider)"
              :detail="connection.accountName ?? undefined"
            >
              <template #trailing>
                <StatusBadge
                  :tone="connectionTone(connection)"
                  :label="connectionLabel(connection)"
                />
              </template>
            </SummaryRow>
          </div>
        </RowGroup>
      </div>
    </div>
  </PanelCard>
</template>
