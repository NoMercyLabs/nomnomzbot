<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import ListEnd from '~icons/nnz/playlist-add';
import Music from '~icons/nnz/note-eighth-pair';
import { useSortable } from '@vueuse/integrations/useSortable';
import type { SortableEvent } from 'sortablejs';
import Grip from '~icons/nnz/hamburger';
import Trash2 from '~icons/nnz/trash';
import { computed, ref, useTemplateRef } from 'vue';

import { useConnections } from '@/api/home';
import {
  useMoveQueueItem,
  usePauseMusic,
  usePromoteQueueItem,
  useMusicQueue,
  useRemoveQueueItem,
  useResumeMusic,
  useSkipMusic,
  type MusicQueue,
  type QueueItem,
} from '@/api/music';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import NowPlayingCard from './components/NowPlayingCard.vue';
import RulesPanel from './components/RulesPanel.vue';

const { channelId } = useCurrentChannel();

const queueQuery = useMusicQueue(channelId);
const connections = useConnections(channelId);
const pause = usePauseMusic(channelId);
const resume = useResumeMusic(channelId);
const skip = useSkipMusic(channelId);
const promote = usePromoteQueueItem(channelId);
const move = useMoveQueueItem(channelId);
const remove = useRemoveQueueItem(channelId);

const nowPlaying = computed<NonNullable<typeof queueQuery.data.value>['nowPlaying']>(
  () => queueQuery.data.value?.nowPlaying ?? null,
);
const rows = computed<QueueItem[]>(
  () => queueQuery.data.value?.queue ?? [],
);

// A song dragged by its handle lands where it was dropped (Stoney: "i need to be able to drag mine to the end").
const queueList = useTemplateRef<HTMLElement>('queueList');

function dropped(event: SortableEvent): void {
  const from = rows.value[event.oldIndex ?? -1];
  const to = rows.value[event.newIndex ?? -1];

  if (from === undefined || to === undefined || from === to) return;

  move.mutate({
    position: Number(from.position),
    toPosition: Number(to.position),
  });
}

useSortable(queueList, rows, {
  handle: '[data-drag-handle]',
  animation: 150,
  onUpdate: dropped,
});

const nowState = usePanelState(queueQuery, (queue: MusicQueue): boolean =>
  !queue.nowPlaying);
const queueState = usePanelState(
  queueQuery,
  (queue: MusicQueue): boolean => queue.queue.length === 0,
);

const spotify = computed<{ tone: StatusTone; key: string }>(() => {
  const status = connections.data.value?.find((item) => item.provider === 'spotify');

  if (!status?.connected)
    return { tone: 'off', key: 'off' };

  return status.needsReauth
    ? { tone: 'bad', key: 'reauth' }
    : { tone: 'ok', key: 'connected' };
});

const removing = ref<QueueItem | null>(null);
const confirmOpen = computed<boolean>({
  get: () => removing.value !== null,
  set: (open: boolean) => {
    if (!open) {
      removing.value = null;
    }
  },
});

function confirmRemove(): void {
  if (removing.value === null) return;

  remove.mutate({ position: Number(removing.value.position) });
  removing.value = null;
}
</script>

<template>
  <PageHeader :title="$t('dashboard.music.title')">
    <template #status>
      <StatusBadge :tone="spotify.tone" :label="$t(`dashboard.music.spotify.${spotify.key}`)" />
    </template>
  </PageHeader>
  <BoardGrid :rows="2">
    <BoardCell :span="5">
      <PanelCard
        tier="lead"
        :title="$t('dashboard.music.now.title')"
        :state="nowState"
        :empty-text="$t('dashboard.music.now.nothing')"
        :error-text="$t('dashboard.music.now.error')"
        @retry="queueQuery.refetch()"
      >
        <NowPlayingCard
          v-if="nowPlaying"
          :track="nowPlaying"
          :observed-at="queueQuery.dataUpdatedAt.value"
          @pause="pause.mutate()"
          @resume="resume.mutate()"
          @skip="skip.mutate()"
        />
      </PanelCard>
    </BoardCell>
    <BoardCell :span="4">
      <PanelCard
        :title="$t('dashboard.music.queue.title')"
        :description="
          rows.length > 0 ? $t('dashboard.music.queue.waiting', { count: rows.length }) : undefined
        "
        :state="queueState"
        :empty-text="$t('dashboard.music.queue.empty')"
        :error-text="$t('dashboard.music.queue.error')"
        @retry="queueQuery.refetch()"
      >
        <div ref="queueList" data-queue-list>
          <SummaryRow
            v-for="item in rows"
            :key="item.position"
            :title="item.trackName"
            :detail="
              item.requestedBy
                ? $t('dashboard.music.queue.by', { artist: item.artist, name: item.requestedBy })
                : $t('dashboard.music.queue.unknownRequester', { artist: item.artist })
            "
            data-queue-item
          >
            <template #leading>
              <span
                data-drag-handle
                :title="$t('dashboard.music.queue.drag')"
                class="-ml-1 flex size-6 flex-none cursor-grab items-center justify-center text-muted-foreground active:cursor-grabbing"
              >
                <Grip class="size-4" aria-hidden="true" />
              </span>
              <img
                v-if="item.imageUrl"
                :src="item.imageUrl"
                alt=""
                class="size-8 flex-none rounded-md object-cover"
              />
              <span
                v-else
                class="flex size-8 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
              >
                <Music class="size-4" aria-hidden="true" />
              </span>
            </template>
            <template #trailing>
              <Button
                variant="outline"
                size="sm"
                :aria-label="$t('dashboard.music.queue.nextRow', { title: item.trackName })"
                @click="promote.mutate({ position: Number(item.position) })"
              >
                <ListEnd aria-hidden="true" />
                {{ $t('dashboard.music.queue.next') }}
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                :aria-label="$t('dashboard.music.queue.remove', { title: item.trackName })"
                @click="removing = item"
              >
                <Trash2 aria-hidden="true" />
              </Button>
            </template>
          </SummaryRow>
        </div>
      </PanelCard>
    </BoardCell>
    <BoardCell :span="3" :rows="2">
      <RulesPanel />
    </BoardCell>
    <BoardCell :span="5">
      <PanelCard
        :title="$t('dashboard.music.history.title')"
        state="empty"
        :empty-text="$t('dashboard.music.history.empty')"
      />
    </BoardCell>
    <BoardCell :span="4">
      <PanelCard
        :title="$t('dashboard.music.thanks.title')"
        state="empty"
        :empty-text="$t('dashboard.music.thanks.empty')"
      />
    </BoardCell>
  </BoardGrid>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.music.queue.confirmTitle')"
    :description="$t('dashboard.music.queue.confirmBody', { title: removing?.trackName ?? '' })"
    :action="$t('dashboard.music.queue.confirmAction')"
    @confirm="confirmRemove"
  />
</template>
