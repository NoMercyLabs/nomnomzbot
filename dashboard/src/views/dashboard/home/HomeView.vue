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
import IconClock from '~icons/nnz/clock';
import IconController from '~icons/nnz/controller';
import IconUserAdd from '~icons/nnz/user-add';
import IconUsers from '~icons/nnz/users';

import { useHomeStats, type HomeStats } from '@/api/home';
import { useStreamHistory } from '@/api/stream';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import { formatCountdown } from '@/components/layout/formatCountdown';
import PageHeader from '@/components/layout/PageHeader.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Badge } from '@/components/ui/badge';
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
const uptime = computed(() => formatCountdown(Number(stats.value?.uptime ?? 0)));

// Each number stands on its own with an icon, so the eye finds it without reading a sentence.
const liveStats = computed(() => [
  {
    key: 'uptime',
    icon: IconClock,
    value: uptime.value,
  },
  {
    key: 'viewers',
    icon: IconUsers,
    value: stats.value?.viewerCount,
  },
  {
    key: 'followers',
    icon: IconUserAdd,
    value: stats.value?.followerCount,
  },
] as const);

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

const panels = computed(() => [
  { id: 'queue', label: t('dashboard.home.queue.title') },
  { id: 'attention', label: t('dashboard.home.attention.title') },
  { id: 'connections', label: t('dashboard.home.connections.title') },
  { id: 'quick', label: t('dashboard.home.quick.title') },
  {
    id: 'happening',
    label: live.value
      ? t('dashboard.home.activity.live')
      : t('dashboard.home.activity.offline'),
  },
] as const satisfies readonly BoardPanel[]);
</script>

<template>
  <PageHeader :title="t('dashboard.home.title')">
    <template v-if="loaded" #status>
      <StatusBadge :tone="live ? 'ok' : 'off'" :label="statusLabel" />
    </template>
    <!-- The header's second row holds the stream line, as the mockup's head-bar does. -->
    <template v-if="hasStreamLine" #bar>
      <ul v-if="live" data-slot="stream-line" class="flex items-center gap-6 text-muted-foreground">
        <li v-for="stat in liveStats" :key="stat.key" class="flex items-center gap-1.5">
          <component :is="stat.icon" class="size-4" aria-hidden="true" />
          <b class="font-semibold tabular-nums text-foreground">{{ stat.value }}</b>
          <span>{{ t(`dashboard.home.liveStats.${stat.key}`) }}</span>
        </li>
        <li v-if="category" class="min-w-0">
          <Badge variant="outline" :title="t('dashboard.home.liveStats.category')" class="gap-1.5 truncate">
            <IconController class="size-3.5" aria-hidden="true" />
            {{ category }}
          </Badge>
        </li>
      </ul>
      <span v-else data-slot="stream-line" class="text-muted-foreground">{{ lastStreamLine }}</span>
    </template>
    <template v-if="primaryRoute" #actions>
      <Button as-child>
        <RouterLink :to="{ name: primaryRoute.name }">{{ primaryRoute.label }}</RouterLink>
      </Button>
    </template>
  </PageHeader>

  <BoardGrid :panels="panels" :rows="2">
    <BoardCell panel="queue" :span="5">
      <ReviewQueuePanel :live="live" />
    </BoardCell>

    <BoardCell panel="attention" :span="4">
      <NeedsAttentionPanel />
    </BoardCell>

    <BoardCell panel="connections" :span="3" :rows="2">
      <ConnectionsPanel />
    </BoardCell>

    <BoardCell panel="quick" :span="5">
      <QuickActionsPanel />
    </BoardCell>

    <BoardCell panel="happening" :span="4">
      <HappeningPanel :live="live" />
    </BoardCell>
  </BoardGrid>
</template>
