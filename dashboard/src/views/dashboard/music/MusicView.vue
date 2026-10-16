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
import IconRules from '~icons/nnz/settings-fader';
import Trash2 from '~icons/nnz/trash';
import Cross from '~icons/nnz/cross';
import { computed, ref, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import { useConnections } from '@/api/home';
import {
  useMoveQueueItem,
  usePauseMusic,
  usePromoteQueueItem,
  useMusicQueue,
  useRemoveQueueItem,
  useResumeMusic,
  useSetRequestsOpen,
  useSkipMusic,
  type MusicQueue,
  type QueueItem,
} from '@/api/music';
import BoardCell from '@/components/layout/BoardCell.vue';
import UserName from '@/components/layout/UserName.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Sheet, SheetClose, SheetContent, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import HistoryPanel from './components/HistoryPanel.vue';
import NowPlayingCard from './components/NowPlayingCard.vue';
import ThanksPanel from './components/ThanksPanel.vue';
import RequesterBulkRemove from './components/RequesterBulkRemove.vue';
import RulesPanel from './components/RulesPanel.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const queueQuery = useMusicQueue(channelId);
const connections = useConnections(channelId);
const pause = usePauseMusic(channelId);
const resume = useResumeMusic(channelId);
const skip = useSkipMusic(channelId);
const promote = usePromoteQueueItem(channelId);
const move = useMoveQueueItem(channelId);
const remove = useRemoveQueueItem(channelId);
const setRequests = useSetRequestsOpen(channelId);
const access = useChannelAccess(channelId);
const canModerateQueue = computed(() => access.can('music:queue:moderate'));
const requestsOpen = computed(() => queueQuery.data.value?.requestsOpen ?? true);

const nowPlaying = computed<NonNullable<typeof queueQuery.data.value>['nowPlaying']>(
  () => queueQuery.data.value?.nowPlaying ?? null,
);
const rows = computed<QueueItem[]>(() => queueQuery.data.value?.queue ?? []);

// A song dragged by its handle lands where it was dropped (Stoney: "i need to be able to drag mine to the end").
const queueList = useTemplateRef<HTMLElement>('queueList');

function dropped(event: SortableEvent): void {
  // Sortable moved the node itself. Put it back so Vue alone orders the rows from the server's answer;
  // a node left where it was dropped plus a keyed patch shows an order the bot does not play.
  event.item.remove();
  event.from.insertBefore(event.item, event.from.children[event.oldIndex ?? 0] ?? null);

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

// Names who asked for the song, so the moderator sees whose request they take away.
const removeNote = computed<string>(() => {
  const name = removing.value?.requestedBy;

  return name
    ? t('dashboard.music.queue.confirmBody', { name })
    : t('dashboard.music.queue.confirmBodyNoName');
});
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

const rulesOpen = ref(false);

const panels = computed(
  () =>
    [
      { id: 'now', label: t('dashboard.music.now.title') },
      { id: 'queue', label: t('dashboard.music.queue.title') },
      { id: 'history', label: t('dashboard.music.history.title') },
      { id: 'thanks', label: t('dashboard.music.thanks.title') },
    ] as const satisfies readonly BoardPanel[],
);
</script>

<template>
  <PageHeader :title="$t('dashboard.music.title')">
    <template #status>
      <StatusBadge :tone="spotify.tone" :label="$t(`dashboard.music.spotify.${spotify.key}`)" />
    </template>
    <!-- The rules are set once and seldom touched, so they slide in on demand instead of taking a column. -->
    <template #actions>
      <Sheet v-model:open="rulesOpen">
        <SheetTrigger as-child>
          <Button variant="outline">
            <IconRules aria-hidden="true" />
            {{ $t("dashboard.music.rules.title") }}
          </Button>
        </SheetTrigger>
        <!-- h-dvh: a phone's browser bars change the visible height, so the footer must follow it. -->
        <SheetContent
          class="gap-0 p-0 data-[side=right]:h-dvh data-[side=right]:max-h-dvh"
          :show-close-button="false"
          :aria-describedby="undefined"
        >
          <SheetTitle class="sr-only">{{ $t("dashboard.music.rules.title") }}</SheetTitle>
          <SheetClose as-child>
            <Button
              variant="ghost"
              size="icon-sm"
              class="absolute top-3 right-3 z-10"
              :aria-label="$t('common.close')"
            >
              <Cross aria-hidden="true" />
            </Button>
          </SheetClose>
          <RulesPanel flush />
        </SheetContent>
      </Sheet>
    </template>
  </PageHeader>
  <BoardGrid :panels="panels" :rows="2">
    <BoardCell panel="now" :span="6">
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
    <BoardCell panel="queue" :span="6">
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
        <RequesterBulkRemove :rows="rows" />
        <template #actions>
          <StatusBadge
            :tone="requestsOpen ? 'ok' : 'off'"
            :label="
              $t(requestsOpen ? 'dashboard.music.queue.open' : 'dashboard.music.queue.closed')
            "
            data-requests-state
          />
          <Button
            v-if="canModerateQueue"
            variant="outline"
            size="sm"
            :disabled="setRequests.isPending.value"
            data-toggle-requests
            @click="setRequests.mutate({ open: !requestsOpen })"
          >
            {{
              $t(
                requestsOpen
                  ? "dashboard.music.queue.closeAction"
                  : "dashboard.music.queue.openAction",
              )
            }}
          </Button>
        </template>
        <div ref="queueList" data-queue-list>
          <SummaryRow
            v-for="item in rows"
            :key="item.position"
            :title="item.trackName"
            :detail="item.artist"
            data-queue-item
          >
            <template v-if="item.requestedBy" #meta>
              <i18n-t keypath="dashboard.music.queue.by" scope="global">
                <template #name>
                  <UserName
                    :user-id="item.requesterId"
                    :name="item.requestedBy"
                    :color="item.requesterColor"
                  />
                </template>
              </i18n-t>
            </template>
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
                class="size-14 flex-none rounded-md object-cover"
              />
              <span
                v-else
                class="flex size-14 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
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
                {{ $t("dashboard.music.queue.next") }}
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
    <BoardCell panel="history" :span="6">
      <HistoryPanel />
    </BoardCell>
    <BoardCell panel="thanks" :span="6">
      <ThanksPanel />
    </BoardCell>
  </BoardGrid>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.music.queue.confirmTitle')"
    :description="removeNote"
    :action="$t('dashboard.music.queue.confirmAction')"
    @confirm="confirmRemove"
  >
    <div v-if="removing" class="flex items-center gap-3 rounded-lg border border-border p-2">
      <img
        v-if="removing.imageUrl"
        :src="removing.imageUrl"
        alt=""
        class="size-12 flex-none rounded-sm object-cover"
      />
      <span
        v-else
        class="flex size-12 flex-none items-center justify-center rounded-sm bg-muted text-muted-foreground"
      >
        <Music class="size-4" aria-hidden="true" />
      </span>
      <span class="grid min-w-0">
        <span data-confirm-song class="truncate text-sm font-semibold">{{
          removing.trackName
        }}</span>
        <span class="truncate text-sm text-muted-foreground">{{ removing.artist }}</span>
      </span>
    </div>
  </ConfirmDialog>
</template>
