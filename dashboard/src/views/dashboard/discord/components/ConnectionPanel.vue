<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The linked server: its name, whether the bot may post there, and the streamer's own on/off for it.
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchDiscordBlastRadius,
  useDisconnectDiscord,
  useRefreshDiscordPresence,
  useRevokeDiscordServerConsent,
  useSetStreamerEnabled,
  type DiscordConnection,
} from '@/api/discord';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';
import { blastLines } from '@/views/dashboard/scripts/components/blastLines';

import { useDiscordInstall } from '../useDiscordInstall';

const props = defineProps<{ connection: DiscordConnection }>();

const { t, te } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const setEnabled = useSetStreamerEnabled(channelId, () => props.connection.id);
const refreshPresence = useRefreshDiscordPresence(channelId, () => props.connection.id);
const { url: installUrl, start: startInstall } = useDiscordInstall(channelId);

// The stored "bot is in the server" flag can be stale (the bot was kicked, or joined later), so the page asks
// the bot once when it opens, and again whenever the person presses Check again.
onMounted(() => {
  if (can('discord:connection:read')) {
    refreshPresence.mutate();
  }
});

const disconnect = useDisconnectDiscord(channelId);
const withdraw = useRevokeDiscordServerConsent(channelId);
const disconnectOpen = ref(false);
const withdrawOpen = ref(false);
const consequences = ref<string[]>([]);
const actionError = ref<string | undefined>(undefined);

// The counted impact is read first; a server whose impact cannot be read is not disconnected.
async function askDisconnect(): Promise<void> {
  actionError.value = undefined;

  try {
    const lines = blastLines(
      await fetchDiscordBlastRadius(channelId.value as string, props.connection.id),
      t,
      te,
    );

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.discord.connection.disconnectNothing')];

    disconnectOpen.value = true;
  } catch (failure) {
    actionError.value = errorText(failure, t);
  }
}

function confirmDisconnect(): void {
  disconnect.mutate(props.connection.id, {
    onSettled: (): void => {
      disconnectOpen.value = false;
    },
    onError: (failure: Error): void => {
      actionError.value = errorText(failure, t);
    },
  });
}

function confirmWithdraw(): void {
  withdraw.mutate(props.connection.id, {
    onSettled: (): void => {
      withdrawOpen.value = false;
    },
    onError: (failure: Error): void => {
      actionError.value = errorText(failure, t);
    },
  });
}

const tone = computed<StatusTone>(() => {
  if (!props.connection.isLinkActive)
    return 'off';

  return props.connection.botInstalled && props.connection.serverConsentStatus === 'approved'
    ? 'ok'
    : 'bad';
});

const label = computed<string>(() => {
  if (!props.connection.botInstalled)
    return t('dashboard.discord.connection.botMissing');

  return props.connection.serverConsentStatus === 'approved'
    ? t('dashboard.discord.connection.ready')
    : t('dashboard.discord.connection.consentPending');
});
</script>

<template>
  <div data-connection class="flex flex-col gap-1">
    <p class="text-sm font-medium">
      {{ connection.guildName ?? connection.guildId }}
    </p>
    <StatusBadge :tone="tone" :label="label" />
    <div
      v-if="!connection.botInstalled && can('discord:connection:write')"
      class="flex flex-col gap-3 py-3"
    >
      <p class="text-sm text-muted-foreground">
        {{ t('dashboard.discord.connection.botMissingHelp') }}
      </p>
      <div class="flex flex-wrap items-center gap-2">
        <Button as-child>
          <a data-add-bot :href="installUrl" @click="startInstall">{{ t('dashboard.discord.setup.add') }}</a>
        </Button>
        <Button
          variant="outline"
          data-check-again
          :disabled="refreshPresence.isPending.value"
          @click="refreshPresence.mutate()"
        >
          {{ t('dashboard.discord.connection.checkAgain') }}
        </Button>
      </div>
    </div>
    <div v-else-if="can('discord:connection:write')" class="py-1">
      <Button variant="ghost" size="sm" as-child>
        <a data-change-server :href="installUrl" @click="startInstall">{{ t('dashboard.discord.connection.change') }}</a>
      </Button>
    </div>
    <div data-streamer-enabled>
      <SettingRow
        :model-value="connection.streamerEnabled"
        :title="t('dashboard.discord.connection.enabled')"
        :description="t('dashboard.discord.connection.enabledHelp')"
        :readonly="!can('discord:connection:write')"
        :disabled="setEnabled.isPending.value"
        @update:model-value="setEnabled.mutate"
      />
    </div>
    <div v-if="can('discord:connection:write')" class="flex flex-wrap items-center gap-2 py-1">
      <Button
        v-if="connection.serverConsentStatus === 'approved'"
        variant="ghost"
        size="sm"
        data-withdraw
        @click="withdrawOpen = true"
      >
        {{ t('dashboard.discord.connection.withdraw') }}
      </Button>
      <Button
        variant="destructive"
        size="sm"
        data-disconnect
        @click="askDisconnect"
      >
        {{ t('dashboard.discord.connection.disconnect') }}
      </Button>
    </div>
    <p v-if="actionError" role="alert" class="text-sm text-destructive">
      {{ actionError }}
    </p>
    <ConfirmDialog
      v-model:open="disconnectOpen"
      :title="t('dashboard.discord.connection.disconnectTitle', { name: connection.guildName ?? connection.guildId })"
      :description="t('dashboard.discord.connection.disconnectDescription')"
      :consequences="consequences"
      :action="t('dashboard.discord.connection.disconnectAction')"
      :pending="disconnect.isPending.value"
      @confirm="confirmDisconnect"
    />
    <ConfirmDialog
      v-model:open="withdrawOpen"
      :title="t('dashboard.discord.connection.withdrawTitle')"
      :description="t('dashboard.discord.connection.withdrawDescription')"
      :action="t('dashboard.discord.connection.withdrawAction')"
      :pending="withdraw.isPending.value"
      @confirm="confirmWithdraw"
    />
  </div>
</template>
