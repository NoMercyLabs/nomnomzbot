<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A person's own account page: their standing in this channel, their pronouns, their linked sign-ins, their own
// voice and a way to their data. Everything is the signed-in person's own; no other viewer appears here.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import { useMyStanding } from '@/api/viewerAccount';
import { useViewerChannel } from '@/api/viewer';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

import ChannelVoicePanel from './ChannelVoicePanel.vue';
import LinkedAccountsPanel from './LinkedAccountsPanel.vue';
import PronounsPanel from './PronounsPanel.vue';

const { t, te } = useI18n();
const route = useRoute();

const channelName = computed<string>(() => String(route.params.channel ?? ''));
const channel = useViewerChannel(channelName);
const channelId = computed<string | null>(() => channel.data.value?.broadcasterId ?? null);
const standing = useMyStanding(channelId);

const shown = computed<string>(() => channel.data.value?.channelName ?? channelName.value);

// A standing is shown by its name, never by its level number.
const standingName = computed<string | null>(() => {
  const value = standing.data.value?.standing;

  if (value === undefined)
    return null;

  const key = `viewer.account.standing.${value}`;

  return te(key)
    ? t(key)
    : t('viewer.account.standing.Everyone');
});
</script>

<template>
  <div class="flex flex-col gap-6 px-6">
    <div class="flex items-center justify-between gap-3">
      <h1 class="text-xl font-semibold">{{ $t('viewer.account.title', { channel: shown }) }}</h1>
      <Badge v-if="standingName" variant="secondary" data-viewer-standing>
        {{ $t('viewer.account.standing.label', { standing: standingName }) }}
      </Badge>
    </div>

    <div v-if="channel.isError.value" class="flex flex-col items-start gap-3" role="alert">
      <p class="text-sm">{{ $t('viewer.account.notFound') }}</p>
      <Button variant="outline" size="sm" @click="channel.refetch()">
        {{ $t('viewer.account.retry') }}
      </Button>
    </div>

    <div v-else class="grid items-start gap-6 lg:grid-cols-2">
      <div class="flex flex-col gap-6">
        <PronounsPanel />
        <LinkedAccountsPanel />
      </div>
      <div class="flex flex-col gap-6">
        <ChannelVoicePanel :channel-id="channelId" />
        <PanelCard
          :title="$t('viewer.account.data.title')"
          :description="$t('viewer.account.data.description')"
          data-viewer-data
        >
          <Button variant="outline" size="sm" as-child>
            <RouterLink :to="{ name: 'dashboard-settings-your-data' }">
              {{ $t('viewer.account.data.open') }}
            </RouterLink>
          </Button>
        </PanelCard>
      </div>
    </div>
  </div>
</template>
