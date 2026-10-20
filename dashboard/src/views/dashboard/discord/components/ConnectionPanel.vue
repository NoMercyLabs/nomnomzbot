<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The linked server: its name, whether the bot may post there, and the streamer's own on/off for it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useSetStreamerEnabled,
  type DiscordConnection,
} from '@/api/discord';
import SettingRow from '@/components/layout/SettingRow.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ connection: DiscordConnection }>();

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const setEnabled = useSetStreamerEnabled(channelId, () => props.connection.id);

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
  </div>
</template>
