<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The public event names a key with the "events" scope can stream, so a tool maker knows what to listen for.
import { computed } from 'vue';

import { useAutomationEvents } from '@/api/automationTokens';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { channelId } = useCurrentChannel();
const events = useAutomationEvents(channelId);

const state = computed<PanelState>(() => {
  if (events.isError.value)
    return 'error';

  if (events.isPending.value)
    return 'loading';

  return (events.data.value ?? []).length === 0
    ? 'empty'
    : 'ready';
});
</script>

<template>
  <PanelCard
    data-panel="events"
    tier="quiet"
    :title="$t('dashboard.settings.apiDevices.events.title')"
    :description="$t('dashboard.settings.apiDevices.events.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.apiDevices.events.empty')"
    :error-text="$t('dashboard.settings.apiDevices.events.error')"
    @retry="events.refetch()"
  >
    <ul class="grid gap-1">
      <li
        v-for="item in events.data.value"
        :key="item.publicName"
        data-event-row
        class="flex min-h-11 flex-wrap items-center gap-x-3 rounded-lg px-3 py-2"
      >
        <code class="font-mono text-sm">{{ item.publicName }}</code>
        <span class="text-sm text-muted-foreground">{{ item.description }}</span>
      </li>
    </ul>
  </PanelCard>
</template>
