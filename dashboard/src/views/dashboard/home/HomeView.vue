<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { useHomeStats, type HomeStats } from '@/api/home';
import { useStreamHistory } from '@/api/stream';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import { formatCountdown } from '@/components/layout/formatCountdown';
import PageHeader from '@/components/layout/PageHeader.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useProviderName } from '@/composables/useProviderName';
import { formatDuration } from '@/views/dashboard/stream/formatDuration';

import ConnectionsPanel from './components/ConnectionsPanel.vue';
import HappeningPanel from './components/HappeningPanel.vue';
import NeedsAttentionPanel from './components/NeedsAttentionPanel.vue';
import QuickActionsPanel from './components/QuickActionsPanel.vue';
import ReviewQueuePanel from './components/ReviewQueuePanel.vue';

const { t, locale } = useI18n();
const router = useRouter();
const { channelId } = useCurrentChannel();
const platformName = useProviderName();

const statsQuery = useHomeStats(channelId);
const historyQuery = useStreamHistory(channelId);

const stats = computed<HomeStats | null>(() => statsQuery.data.value ?? null);
// Until the stats arrive the header claims nothing: no Offline badge, no stream line, no action.
const loaded = computed(() => stats.value !== null);
const live = computed(() => stats.value?.isLive === true);

const statusLabel = computed<string>(() =>
  live.value
    ? t('dashboard.home.live', {
        platforms: (stats.value?.platformsLive ?? []).map(platformName).join(', '),
      })
    : t('dashboard.home.offline'),
);

const category = computed(() => stats.value?.gameName ?? '');
const liveLineKey = computed<string>(() =>
  category.value
    ? 'dashboard.home.liveLineCategory'
    : 'dashboard.home.liveLine',
);
const uptime = computed(() => formatCountdown(Number(stats.value?.uptime ?? 0)));

// A stream the bot never closed has no end or length, so offline shows nothing rather than a guess.
const lastStream = computed(() =>
  historyQuery.data.value?.find((past) => past.endedAt && past.durationSeconds != null),
);

const lastStreamLine = computed<string>(() => {
  const past = lastStream.value;

  if (!past?.endedAt)
    return '';

  const ended = new Date(past.endedAt);

  return t('dashboard.home.lastStream', {
    date: new Intl.DateTimeFormat(locale.value, {
      weekday: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    }).format(ended),
    length: formatDuration(Number(past.durationSeconds)),
  });
});

const hasStreamLine = computed(() => loaded.value && (live.value || lastStreamLine.value !== ''));

// The one primary action follows the stream: chat while live, the stream setup while offline.
const primaryRoute = computed<{ name: string; label: string } | null>(() => {
  if (!loaded.value)
    return null;

  const next: { name: string; label: string } = live.value
    ? { name: 'dashboard-chat', label: t('dashboard.home.openChat') }
    : { name: 'dashboard-stream', label: t('dashboard.home.editStream') };

  return router.hasRoute(next.name)
    ? next
    : null;
});
</script>

<template>
  <PageHeader :title="t('dashboard.home.title')">
    <template v-if="loaded" #status>
      <StatusBadge :tone="live ? 'ok' : 'off'" :label="statusLabel" />
    </template>
    <!-- The header's second row holds the stream line, as the mockup's head-bar does. -->
    <template v-if="hasStreamLine" #tabs>
      <span data-slot="stream-line" class="text-muted-foreground">
        <i18n-t v-if="live" :keypath="liveLineKey" scope="global" tag="span">
          <template #uptime>
            <b class="font-semibold text-foreground">{{ uptime }}</b>
          </template>
          <template #viewers>
            <b class="font-semibold text-foreground">{{ stats?.viewerCount }}</b>
          </template>
          <template #followers>
            <b class="font-semibold text-foreground">{{ stats?.followerCount }}</b>
          </template>
          <template #category>{{ category }}</template>
        </i18n-t>
        <template v-else>{{ lastStreamLine }}</template>
      </span>
    </template>
    <template v-if="primaryRoute" #actions>
      <Button as-child>
        <RouterLink :to="{ name: primaryRoute.name }">{{ primaryRoute.label }}</RouterLink>
      </Button>
    </template>
  </PageHeader>

  <BoardGrid :rows="2">
    <BoardCell :span="5">
      <ReviewQueuePanel :live="live" />
    </BoardCell>

    <BoardCell :span="4">
      <NeedsAttentionPanel />
    </BoardCell>

    <BoardCell :span="3" :rows="2">
      <ConnectionsPanel />
    </BoardCell>

    <BoardCell :span="5">
      <QuickActionsPanel />
    </BoardCell>

    <BoardCell :span="4">
      <HappeningPanel :live="live" />
    </BoardCell>
  </BoardGrid>
</template>
