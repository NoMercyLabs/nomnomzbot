<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The outside services of the channel (Spotify, YouTube, Kick, Patreon, ...). Each row shows whether the
// service is connected and as whom, whether it needs a fresh sign-in, and the permission sets it holds. A
// service connects through its own sign-in page. Disconnecting first shows what stops working, with the real
// counts the bot reads right then. Twitch is the main account and never disconnects here.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import ChevronDown from '~icons/nnz/chevron-down';

import { useConnections, type IntegrationStatus } from '@/api/home';
import {
  connectScopeSets,
  extraScopeSets,
  reconnectScopeSet,
  useConnectProvider,
  useDisconnectBlastRadius,
  useDisconnectIntegration,
  useIntegrations,
  type BlastRadius,
  type ChannelIntegration,
} from '@/api/integrations';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import ConnectionRow, { type ConnectionState } from '@/components/layout/ConnectionRow.vue';
import ProviderMark from '@/components/layout/ProviderMark.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useConnectWindow } from '@/composables/useConnectWindow';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

import SettingsPageFrame from '../components/SettingsPageFrame.vue';
import SpotifyCredentialsPanel from './components/SpotifyCredentialsPanel.vue';

const primaryAccount = 'twitch';

const { t, te } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const integrations = useIntegrations(channelId);
const statuses = useConnections(channelId);
const connect = useConnectProvider(channelId);
const connectWindow = useConnectWindow(channelId);
const impact = useDisconnectBlastRadius(channelId);
const disconnect = useDisconnectIntegration(channelId);

const error = ref<string | undefined>(undefined);
const shopDomain = ref('');
const leaving = ref<ChannelIntegration | null>(null);
const consequences = ref<string[]>([]);

const rows = computed(() => integrations.data.value ?? []);

const state = computed<PanelState>(() => {
  if (integrations.isError.value)
    return 'error';

  if (integrations.isPending.value)
    return 'loading';

  return rows.value.length === 0
    ? 'empty'
    : 'ready';
});

function statusOf(row: ChannelIntegration): IntegrationStatus | undefined {
  return (statuses.data.value ?? []).find((item) => item.provider === row.id);
}

// A service that needs a fresh sign-in draws as attention: the streamer can fix it, so it shows here.
function stateOf(row: ChannelIntegration): ConnectionState {
  if (!row.connected)
    return 'disconnected';

  const status = statusOf(row);

  return status?.needsReauth || status?.decryptFailed
    ? 'attention'
    : 'connected';
}

// The exact reason a connected service is not plain "Connected"; the row words the plain states itself.
function reasonOf(row: ChannelIntegration): string | undefined {
  const status = statusOf(row);

  if (!row.connected)
    return undefined;

  if (status?.decryptFailed)
    return t('dashboard.settings.integrations.decryptFailed');

  if (status?.needsReauth)
    return t('dashboard.settings.integrations.needsReauth');

  return status?.loginOnly
    ? t('dashboard.settings.integrations.loginOnly')
    : undefined;
}

function accountOf(row: ChannelIntegration): string | undefined {
  return row.connectedAs ?? statusOf(row)?.accountName ?? undefined;
}

// A key the server sends shows by its translated name; an unknown key falls back to plain words, never the raw key.
function plainName(group: 'scopeSetNames' | 'capabilityNames', key: string): string {
  const path = `dashboard.settings.integrations.${group}.${key.replaceAll('.', '_')}`;

  return te(path)
    ? t(path)
    : (key.split('.').at(-1) ?? key).replaceAll('_', ' ');
}

function scopesOf(row: ChannelIntegration): string {
  const sets = statusOf(row)?.grantedScopeSets ?? [];

  return sets.length === 0
    ? ''
    : t('dashboard.settings.integrations.scopeSets', { sets: sets.map((set) => plainName('scopeSetNames', set)).join(', ') });
}

function capabilitiesOf(row: ChannelIntegration): string[] {
  const map = statusOf(row)?.capabilities ?? {};

  return Object.entries(map)
    .filter(([, granted]) => granted)
    .map(([name]) => plainName('capabilityNames', name));
}

// The permissions a connected service can still add, such as the YouTube member list and the analytics.
function offersOf(row: ChannelIntegration) {
  const status = statusOf(row);

  if (!row.connected || !status)
    return [] as const satisfies readonly string[];

  return (extraScopeSets[row.id] ?? []).filter((set) =>
    !status.grantedScopeSets.includes(set));
}

function hasDetails(row: ChannelIntegration): boolean {
  return Boolean(row.lastSync) ||
    scopesOf(row) !== '' ||
    capabilitiesOf(row).length > 0 ||
    offersOf(row).length > 0;
}

function canConnect(row: ChannelIntegration): boolean {
  return row.id in connectScopeSets || Boolean(row.oauthUrl);
}

function canDisconnect(row: ChannelIntegration): boolean {
  return row.connected && row.id !== primaryAccount;
}

function hasActions(row: ChannelIntegration): boolean {
  return can('integration:write') && (canConnect(row) || canDisconnect(row));
}

function connectLabel(row: ChannelIntegration): string {
  return row.connected
    ? t('dashboard.settings.integrations.reconnect')
    : t('dashboard.settings.integrations.connect');
}

function needsShop(row: ChannelIntegration): boolean {
  return row.id === 'shopify';
}

async function open(row: ChannelIntegration, scopeSetKey?: string): Promise<void> {
  error.value = undefined;

  try {
    const url = row.id in connectScopeSets
      ? await connect.mutateAsync({
          provider: row.id,
          scopeSetKey: scopeSetKey ?? (row.connected
            ? reconnectScopeSet(row.id)
            : undefined),
          shopDomain: needsShop(row)
            ? shopDomain.value.trim()
            : undefined,
        })
      : (row.oauthUrl as string);

    connectWindow(url);
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

function categoryLine(category: {
  categoryKey: string;
  count: number | string;
  sample: string[];
}, minimum: boolean): string {
  const name = te(category.categoryKey)
    ? t(category.categoryKey)
    : category.categoryKey;

  const count = minimum
    ? t('dashboard.settings.integrations.atLeast', { count: category.count })
    : String(category.count);

  const names = category.sample.length === 0
    ? ''
    : ` (${category.sample.join(', ')})`;

  return `${name}: ${count}${names}`;
}

function consequencesOf(radius: BlastRadius): string[] {
  return radius.categories.map((category) => categoryLine(category, radius.isMinimum));
}

async function askToLeave(row: ChannelIntegration): Promise<void> {
  error.value = undefined;

  try {
    const radius = await impact.mutateAsync(row.id);

    consequences.value = consequencesOf(radius);
    leaving.value = row;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function confirmLeave(): Promise<void> {
  const row = leaving.value;

  if (row === null) return;

  try {
    await disconnect.mutateAsync(row.id);
    toast.success(t('dashboard.settings.integrations.disconnected', { name: row.name }));
    leaving.value = null;
  } catch (failure) {
    leaving.value = null;
    error.value = errorText(failure, t);
  }
}

function setLeavingOpen(open: boolean): void {
  if (!open) {
    leaving.value = null;
  }
}
</script>

<template>
  <SettingsPageFrame>
    <PanelCard
      data-view="integrations"
      pad
      :title="$t('dashboard.settings.integrations.title')"
      :description="$t('dashboard.settings.integrations.description')"
      :state="state"
      :error-text="$t('dashboard.settings.integrations.loadFailed')"
      :empty-text="$t('dashboard.settings.integrations.empty')"
      @retry="integrations.refetch()"
    >
      <RowGroup>
        <ConnectionRow
          v-for="row in rows"
          :key="row.id"
          :data-integration="row.id"
          :provider="row.id"
          :name="row.name"
          :state="stateOf(row)"
          :label="reasonOf(row)"
          :account="accountOf(row)"
          :detail="row.description"
        >
          <template #leading>
            <ProviderMark :provider="row.id" />
          </template>
          <template v-if="row.id === 'spotify' && can('integration:write')" #credential>
            <SpotifyCredentialsPanel />
          </template>
          <template v-if="hasDetails(row)" #detail>
            <Collapsible>
              <CollapsibleTrigger as-child>
                <Button variant="ghost" size="sm" class="-ml-2 mt-1 text-muted-foreground" data-action="details">
                  {{ $t('dashboard.settings.integrations.details') }}
                  <ChevronDown class="size-4" aria-hidden="true" />
                </Button>
              </CollapsibleTrigger>
              <CollapsibleContent>
                <div class="grid gap-1 border-l border-border pl-3 text-sm text-muted-foreground">
                  <p v-if="scopesOf(row)" data-slot="scope-sets">{{ scopesOf(row) }}</p>
                  <p v-if="capabilitiesOf(row).length > 0" data-slot="capabilities">
                    {{ $t('dashboard.settings.integrations.capabilities', { list: capabilitiesOf(row).join(', ') }) }}
                  </p>
                  <p v-if="row.lastSync">
                    {{ $t('dashboard.settings.integrations.lastSync', { date: time.dateTime(row.lastSync) }) }}
                  </p>
                  <div
                    v-if="offersOf(row).length > 0 && can('integration:write')"
                    data-slot="offers"
                    class="flex flex-wrap items-center gap-2 pt-1"
                  >
                    <span>{{ $t('dashboard.settings.integrations.offers') }}</span>
                    <Button
                      v-for="set in offersOf(row)"
                      :key="set"
                      variant="outline"
                      size="sm"
                      data-action="allow"
                      :data-scope-set="set"
                      :disabled="connect.isPending.value"
                      @click="open(row, set)"
                    >
                      {{ plainName('scopeSetNames', set) }}
                    </Button>
                  </div>
                </div>
              </CollapsibleContent>
            </Collapsible>
          </template>
          <template v-if="hasActions(row)" #actions>
            <Input
              v-if="needsShop(row) && !row.connected"
              v-model="shopDomain"
              class="h-9 w-48 md:h-8"
              name="shopDomain"
              :placeholder="$t('dashboard.settings.integrations.shopDomain')"
              :aria-label="$t('dashboard.settings.integrations.shopDomain')"
            />
            <Button
              v-if="canDisconnect(row)"
              variant="ghost"
              size="sm"
              class="text-destructive hover:text-destructive"
              data-action="disconnect"
              :disabled="impact.isPending.value"
              @click="askToLeave(row)"
            >
              {{ $t('dashboard.settings.integrations.disconnect') }}
            </Button>
            <Button
              v-if="canConnect(row)"
              :variant="stateOf(row) === 'connected' ? 'outline' : 'default'"
              size="sm"
              data-action="connect"
              :disabled="connect.isPending.value || (needsShop(row) && shopDomain.trim() === '' && !row.connected)"
              @click="open(row)"
            >
              {{ connectLabel(row) }}
            </Button>
          </template>
        </ConnectionRow>
      </RowGroup>
      <InlineNotice v-if="error" tone="error" compact class="px-3 pt-3">{{ error }}</InlineNotice>
    </PanelCard>

    <ConfirmDialog
      :open="leaving !== null"
      :title="$t('dashboard.settings.integrations.confirm.title', { name: leaving?.name ?? '' })"
      :description="$t('dashboard.settings.integrations.confirm.description')"
      :consequences="consequences"
      :action="$t('dashboard.settings.integrations.disconnect')"
      :pending="disconnect.isPending.value"
      @update:open="setLeavingOpen"
      @confirm="confirmLeave"
    />
  </SettingsPageFrame>
</template>
