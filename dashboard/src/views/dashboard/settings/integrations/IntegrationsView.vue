<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The outside services of the channel (Spotify, YouTube, Kick, Patreon, ...). Each row shows whether the
// service is connected and as whom, whether it needs a fresh sign-in, and the permission sets it holds. A
// service connects through its own sign-in page. Disconnecting first shows what stops working, with the real
// counts the bot reads right then. Twitch is the main account and never disconnects here.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useConnections, type IntegrationStatus } from '@/api/home';
import {
  connectScopeSets,
  useConnectProvider,
  useDisconnectBlastRadius,
  useDisconnectIntegration,
  useIntegrations,
  type BlastRadius,
  type ChannelIntegration,
} from '@/api/integrations';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';
import { errorText } from '@/lib/errors';

import SpotifyCredentialsPanel from './components/SpotifyCredentialsPanel.vue';

const primaryAccount = 'twitch';

const { t, te } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const integrations = useIntegrations(channelId);
const statuses = useConnections(channelId);
const connect = useConnectProvider(channelId);
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

function toneOf(row: ChannelIntegration): StatusTone {
  if (!row.connected)
    return 'off';

  return statusOf(row)?.needsReauth
    ? 'bad'
    : 'ok';
}

function labelOf(row: ChannelIntegration): string {
  const status = statusOf(row);

  if (!row.connected)
    return t('dashboard.settings.integrations.notConnected');

  if (status?.decryptFailed)
    return t('dashboard.settings.integrations.decryptFailed');

  if (status?.needsReauth)
    return t('dashboard.settings.integrations.needsReauth');

  return status?.loginOnly
    ? t('dashboard.settings.integrations.loginOnly')
    : t('dashboard.settings.integrations.connected');
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

function canConnect(row: ChannelIntegration): boolean {
  return row.id in connectScopeSets || Boolean(row.oauthUrl);
}

function canDisconnect(row: ChannelIntegration): boolean {
  return row.connected && row.id !== primaryAccount;
}

function connectLabel(row: ChannelIntegration): string {
  return row.connected
    ? t('dashboard.settings.integrations.reconnect')
    : t('dashboard.settings.integrations.connect');
}

function needsShop(row: ChannelIntegration): boolean {
  return row.id === 'shopify';
}

async function open(row: ChannelIntegration): Promise<void> {
  error.value = undefined;

  try {
    const url = row.id in connectScopeSets
      ? await connect.mutateAsync({ provider: row.id, shopDomain: needsShop(row)
          ? shopDomain.value.trim()
          : undefined })
      : (row.oauthUrl as string);

    window.location.assign(url);
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
  <PageHeader :title="$t('frame.nav.settingsPages.integrations')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
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
        <div
          v-for="row in rows"
          :key="row.id"
          :data-integration="row.id"
          class="flex min-h-11 flex-wrap items-center gap-x-4 gap-y-2 rounded-[8px] px-3 py-2"
        >
          <div class="min-w-0 flex-1 basis-64">
            <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span class="text-sm font-medium wrap-break-word">{{ row.name }}</span>
              <StatusBadge :tone="toneOf(row)" :label="labelOf(row)" />
            </span>
            <span class="block text-sm text-muted-foreground">{{ row.description }}</span>
            <span v-if="accountOf(row)" class="block text-sm text-muted-foreground">
              {{ $t('dashboard.settings.integrations.account', { account: accountOf(row) }) }}
            </span>
            <span v-if="row.lastSync" class="block text-sm text-muted-foreground">
              {{ $t('dashboard.settings.integrations.lastSync', { date: time.dateTime(row.lastSync) }) }}
            </span>
            <span v-if="scopesOf(row)" data-slot="scope-sets" class="block text-sm text-muted-foreground">{{ scopesOf(row) }}</span>
            <span v-if="capabilitiesOf(row).length > 0" data-slot="capabilities" class="block text-sm text-muted-foreground">
              {{ $t('dashboard.settings.integrations.capabilities', { list: capabilitiesOf(row).join(', ') }) }}
            </span>
            <SpotifyCredentialsPanel v-if="row.id === 'spotify' && can('integration:write')" />
          </div>
          <div v-if="can('integration:write')" class="flex flex-wrap items-center justify-end gap-2">
            <Input
              v-if="needsShop(row) && !row.connected"
              v-model="shopDomain"
              class="h-8 w-48"
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
              :variant="row.connected && !statusOf(row)?.needsReauth ? 'secondary' : 'default'"
              size="sm"
              data-action="connect"
              :disabled="connect.isPending.value || (needsShop(row) && shopDomain.trim() === '' && !row.connected)"
              @click="open(row)"
            >
              {{ connectLabel(row) }}
            </Button>
          </div>
        </div>
      </RowGroup>
      <p v-if="error" role="alert" class="px-3 pt-3 text-sm text-destructive">{{ error }}</p>
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
  </PageBody>
</template>
