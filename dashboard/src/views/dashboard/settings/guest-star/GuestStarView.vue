<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Twitch Guest Star for the channel. The settings and the session card always show. The invites and the slots
// belong to a running session, so they show only while one runs.
import { computed } from 'vue';

import { useGuestStarSession } from '@/api/guestStar';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import SettingsPageFrame from '../components/SettingsPageFrame.vue';
import InvitesPanel from './components/InvitesPanel.vue';
import SessionPanel from './components/SessionPanel.vue';
import SettingsPanel from './components/SettingsPanel.vue';
import SlotsPanel from './components/SlotsPanel.vue';

const { channelId } = useCurrentChannel();

const session = useGuestStarSession(channelId);
const state = usePanelState(session);
const running = computed(() => session.data.value ?? null);
</script>

<template>
  <SettingsPageFrame :crumb="$t('frame.group.settings.connections')">
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.guestStar.explain") }}</p>
    <SettingsPanel />
    <SessionPanel :session="running" :state="state" @retry="session.refetch()" />
    <template v-if="running !== null">
      <InvitesPanel :session-id="running.id" />
      <SlotsPanel :session-id="running.id" :guests="running.guests" />
    </template>
  </SettingsPageFrame>
</template>
