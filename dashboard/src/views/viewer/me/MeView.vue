<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A viewer's own page in one channel: their tiles and their songs in the queue. Both reads are the
// viewer's own data only. The queue pushes come through the viewer music group, which needs a sign-in only,
// so an open or closed queue shows at once.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import {
  useViewerChannel,
  useViewerQueue,
  useViewerSummary,
  type ViewerQueuedSong,
  type ViewerSummary,
} from '@/api/viewer';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';
import { useViewerMusicLive } from '@/realtime/useViewerMusicLive';
import { formatClock } from '@/views/dashboard/music/components/formatClock';

const { t } = useI18n();
const time = useTime();
const route = useRoute();

const channelName = computed<string>(() => String(route.params.channel ?? ''));
const channel = useViewerChannel(channelName);
const channelId = computed<string | null>(() => channel.data.value?.broadcasterId ?? null);

useViewerMusicLive(channelId);

const requestsClosed = computed<boolean>(
  () => channel.data.value?.isAcceptingRequests === false,
);

const summary = useViewerSummary(channelId);
const queue = useViewerQueue(channelId);
const queuePanel = usePanelState(queue, (data) =>
  data.songs.length === 0);

const shown = computed<string>(() => channel.data.value?.channelName ?? channelName.value);
const songs = computed<readonly ViewerQueuedSong[]>(() => queue.data.value?.songs ?? []);

function day(iso: string | null | undefined): string {
  return time.date(iso);
}

function followLine(data: ViewerSummary): string {
  return data.followedAt
    ? day(data.followedAt)
    : t('viewer.me.stats.notFollowing');
}

function subLine(data: ViewerSummary): string {
  return data.subTier
    ? t('viewer.me.stats.tier', { tier: data.subTier })
    : t('viewer.me.stats.notSubbed');
}

function liveLine(data: ViewerSummary): string {
  return data.isLive
    ? t('viewer.me.stats.liveYes')
    : t('viewer.me.stats.liveNo');
}

function place(song: ViewerQueuedSong): string {
  const position = Number(song.position);

  return t('viewer.me.queue.position', { n: position }, position);
}

function retry(): void {
  void channel.refetch();
  void summary.refetch();
  void queue.refetch();
}
</script>

<template>
  <div class="flex flex-col gap-6 px-6">
    <div class="flex items-center justify-between gap-3">
      <h1 class="text-xl font-semibold">{{ $t('viewer.me.title', { channel: shown }) }}</h1>
      <Button v-if="channel.data.value"
              variant="outline"
              size="sm"
              as-child
              data-viewer-chat-link
      >
        <RouterLink :to="{ name: 'viewer-chat', params: { channel: channelName } }">
          {{ $t('viewer.me.chat') }}
        </RouterLink>
      </Button>
    </div>

    <div v-if="channel.isError.value" class="flex flex-col items-start gap-3" role="alert">
      <p class="text-sm">{{ $t('viewer.me.notFound') }}</p>
      <Button variant="outline" size="sm" @click="retry">{{ $t('viewer.me.retry') }}</Button>
    </div>

    <template v-else>
      <p v-if="summary.isError.value" class="text-sm text-destructive" role="alert">
        {{ $t('viewer.me.stats.error') }}
      </p>
      <StatStrip v-else-if="summary.data.value" data-viewer-stats>
        <StatTile
          :label="$t('viewer.me.stats.messages')"
          :value="Number(summary.data.value.totalMessages)"
        />
        <StatTile
          :label="$t('viewer.me.stats.commands')"
          :value="Number(summary.data.value.totalCommandsUsed)"
        />
        <StatTile
          v-if="summary.data.value.songRequestsEnabled"
          :label="$t('viewer.me.stats.songs')"
          :value="Number(summary.data.value.totalSongRequests)"
        />
        <StatTile
          v-if="summary.data.value.pointsEnabled"
          :label="$t('viewer.me.stats.points')"
          :value="Number(summary.data.value.balance ?? 0)"
        />
        <StatTile
          :label="$t('viewer.me.stats.follow')"
          :value="followLine(summary.data.value)"
        />
        <StatTile :label="$t('viewer.me.stats.sub')" :value="subLine(summary.data.value)" />
        <StatTile :label="$t('viewer.me.stats.live')" :value="liveLine(summary.data.value)" />
      </StatStrip>

      <p
        v-if="requestsClosed"
        class="rounded-lg border bg-muted p-3 text-sm text-muted-foreground"
        role="status"
        data-queue-closed
      >
        {{ $t('viewer.me.queue.closed') }}
      </p>

      <PanelCard
        v-if="summary.data.value?.songRequestsEnabled !== false"
        :title="$t('viewer.me.queue.title')"
        :description="$t('viewer.me.queue.description')"
        :state="queuePanel"
        :empty-text="$t('viewer.me.queue.empty')"
        :error-text="$t('viewer.me.queue.error')"
        data-viewer-queue
        @retry="queue.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="song in songs"
            :key="song.code ?? song.position"
            :title="song.trackName"
            :detail="song.artist"
            :meta="formatClock(Number(song.durationMs))"
          >
            <template #end>
              <span class="pr-3 text-sm font-medium tabular-nums">{{ place(song) }}</span>
            </template>
          </SummaryRow>
        </RowGroup>
        <template v-if="queue.data.value" #footer>
          <p class="text-sm text-muted-foreground">
            {{ $t('viewer.me.queue.total', { count: queue.data.value.queueLength }) }}
          </p>
        </template>
      </PanelCard>
    </template>
  </div>
</template>
