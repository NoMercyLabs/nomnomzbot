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
import Plus from '~icons/nnz/add';
import { useSortable } from '@vueuse/integrations/useSortable';
import type { SortableEvent } from 'sortablejs';
import Grip from '~icons/nnz/hamburger';
import IconRules from '~icons/nnz/settings-fader';
import Trash2 from '~icons/nnz/trash';
import Ban from '~icons/nnz/do-not-enter';
import { computed, ref, useTemplateRef, type ComponentPublicInstance } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import { useConnections } from '@/api/home';
import {
  useBanQueueItem,
  useMoveQueueItem,
  usePauseMusic,
  usePromoteQueueItem,
  useMusicConfig,
  useMusicQueue,
  useRemoveQueueItem,
  useResumeMusic,
  useSaveMusicConfig,
  useSetRequestsOpen,
  useSkipMusic,
  type MusicQueue,
  type QueueItem,
} from '@/api/music';
import { MAX_PLAYER_BUTTONS, usePlayerButtons } from '@/api/playerButtons';
import BoardCell from '@/components/layout/BoardCell.vue';
import UserName from '@/components/layout/UserName.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import { Button } from '@/components/ui/button';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useBoard } from '@/composables/useBoard';
import { usePanelState } from '@/composables/usePanelState';

import { musicMinimums } from './components/boardPanels';
import HistoryPanel from './components/HistoryPanel.vue';
import NowPlayingCard from './components/NowPlayingCard.vue';
import PlayerButtonPicker from './components/PlayerButtonPicker.vue';
import PlayerButtons from './components/PlayerButtons.vue';
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
const ban = useBanQueueItem(channelId);
const setRequests = useSetRequestsOpen(channelId);
const access = useChannelAccess(channelId);
const canModerateQueue = computed(() => access.can('music:queue:moderate'));
const requestsOpen = computed(() => queueQuery.data.value?.requestsOpen);
const canWriteConfig = computed(() => access.can('music:config:write'));

// The panel header's Add button opens the player button picker (Stoney: "add a pannel header button").
const addingButton = ref(false);
const playerButtons = usePlayerButtons(() =>
  canWriteConfig.value
    ? channelId.value
    : null);
const buttonsFull = computed(() =>
  (playerButtons.data.value?.length ?? 0) >= MAX_PLAYER_BUTTONS);
const configQuery = useMusicConfig(channelId);
const saveConfig = useSaveMusicConfig(channelId);
const playbackOn = computed(() => configQuery.data.value?.isEnabled);

function setPlayback(on: boolean): void {
  saveConfig.mutate({ isEnabled: on });
}

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
const banning = ref(false);

function askRemove(item: QueueItem): void {
  banning.value = false;
  removing.value = item;
}

function askBan(item: QueueItem): void {
  banning.value = true;
  removing.value = item;
}

const confirmKeys = computed(() =>
  banning.value
    ? ({
        title: 'banTitle',
        body: 'banBody',
        bodyNoName: 'banBodyNoName',
        action: 'banAction',
      } as const)
    : ({
        title: 'confirmTitle',
        body: 'confirmBody',
        bodyNoName: 'confirmBodyNoName',
        action: 'confirmAction',
      } as const),
);

// Names who asked for the song, so the moderator sees whose request they take away.
const removeNote = computed<string>(() => {
  const name = removing.value?.requestedBy;
  const keys = confirmKeys.value;

  return name
    ? t(`dashboard.music.queue.${keys.body}`, { name })
    : t(`dashboard.music.queue.${keys.bodyNoName}`);
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

  const position = Number(removing.value.position);

  if (banning.value) {
    ban.mutate({ position });
  } else {
    remove.mutate({ position });
  }

  removing.value = null;
}

const rulesOpen = ref(false);

// Stoney 2026-10-18: "the breakpoints need to be better defined based on the other component's needs". Each panel
// declares the narrowest column it renders properly in, so the board counts its columns from them.
// Stoney 2026-10-19: "i asked you to make it a togglable menu to prevent this crap!" The rules are never a column:
// they slide over the page from the right, closed by default, opened by the header button, and closed by its close
// button, Escape or the browser's Back.
const board = useTemplateRef<ComponentPublicInstance>('board');
const canSeeRules = computed(() => access.can('music:config:read'));

const panels = computed<BoardPanel[]>(() => {
  const shown: BoardPanel[] = [
    {
      id: 'now',
      label: t('dashboard.music.now.title'),
      minWidth: musicMinimums.now,
    },
    {
      id: 'queue',
      label: t('dashboard.music.queue.title'),
      minWidth: musicMinimums.queue,
    },
  ];

  if (canSeeRules.value) {
    shown.push(
      {
        id: 'history',
        label: t('dashboard.music.history.title'),
        minWidth: musicMinimums.history,
      },
      {
        id: 'thanks',
        label: t('dashboard.music.thanks.title'),
        minWidth: musicMinimums.thanks,
      },
    );
  }

  return shown;
});

const layout = useBoard('music', board, () => panels.value);
const span = layout.span;
</script>

<template>
  <PageHeader :title="$t('dashboard.music.title')">
    <template #status>
      <StatusBadge :tone="spotify.tone" :label="$t(`dashboard.music.spotify.${spotify.key}`)" />
    </template>
    <template #actions>
      <Button v-if="canSeeRules" variant="outline" @click="rulesOpen = true">
        <IconRules aria-hidden="true" />
        {{ $t("dashboard.music.rules.title") }}
      </Button>
    </template>
  </PageHeader>
  <PanelBeside>
    <BoardGrid ref="board" :panels="panels" :board="layout" :rows="2">
      <BoardCell panel="now" :span="span">
        <PanelCard
          tier="lead"
          :title="$t('dashboard.music.now.title')"
          :state="nowState"
          :empty-text="$t('dashboard.music.now.nothing')"
          :error-text="$t('dashboard.music.now.error')"
          @retry="queueQuery.refetch()"
        >
          <!-- Music on/off is not a request rule: it stops all playback, so it sits with the player it switches.
             Stoney 2026-10-19: a page menu holding only this one item was an "extra useless dropdown". -->
          <template v-if="canWriteConfig" #actions>
            <Button
              data-player-button-add
              variant="outline"
              size="sm"
              :disabled="buttonsFull"
              :title="buttonsFull ? $t('dashboard.music.now.buttons.full', { max: MAX_PLAYER_BUTTONS }) : undefined"
              @click="addingButton = true"
            >
              <Plus class="size-5" aria-hidden="true" />
              {{ $t('dashboard.music.now.buttons.add') }}
            </Button>
            <LabeledSwitch
              v-if="playbackOn !== undefined"
              data-music-playback
              :model-value="playbackOn"
              :label="$t('dashboard.music.playback.label')"
              :help="$t('dashboard.music.playback.help')"
              :disabled="saveConfig.isPending.value"
              @update:model-value="setPlayback"
            />
          </template>
          <NowPlayingCard
            v-if="nowPlaying"
            :track="nowPlaying"
            :observed-at="queueQuery.dataUpdatedAt.value"
            @pause="pause.mutate()"
            @resume="resume.mutate()"
            @skip="skip.mutate()"
          >
            <template #buttons>
              <PlayerButtons :track="nowPlaying" />
            </template>
          </NowPlayingCard>
          <!-- Nothing playing: the card shows its empty line and the buttons stay, so Play works and they can be arranged. -->
          <template v-if="!nowPlaying" #footer>
            <PlayerButtons :track="null" />
          </template>
        </PanelCard>
        <!-- Outside the card: an empty card renders no body, and the Add button works with nothing playing. -->
        <PlayerButtonPicker
          v-if="canWriteConfig"
          v-model:open="addingButton"
          :buttons="playerButtons.data.value ?? []"
          :index="null"
        />
      </BoardCell>
      <BoardCell panel="queue" :span="span">
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
              v-if="requestsOpen !== undefined"
              :tone="requestsOpen ? 'ok' : 'off'"
              :label="
                $t(requestsOpen ? 'dashboard.music.queue.open' : 'dashboard.music.queue.closed')
              "
              data-requests-state
            />
            <Button
              v-if="canModerateQueue && requestsOpen !== undefined"
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
              truncate
              wrap
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
                  v-if="canModerateQueue"
                  data-drag-handle
                  :title="$t('dashboard.music.queue.drag')"
                  class="-ml-1 flex size-6 flex-none cursor-grab items-center justify-center text-muted-foreground active:cursor-grabbing"
                >
                  <Grip class="size-4" aria-hidden="true" />
                </span>
                <span
                  data-queue-number
                  class="w-8 flex-none text-sm tabular-nums text-muted-foreground"
                >
                  {{ `#${item.number}` }}
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
              <template v-if="canModerateQueue" #trailing>
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
                  @click="askRemove(item)"
                >
                  <Trash2 aria-hidden="true" />
                </Button>
                <Button
                  v-if="canModerateQueue"
                  variant="ghost"
                  size="icon"
                  :aria-label="$t('dashboard.music.queue.banRow', { title: item.trackName })"
                  data-ban-song
                  @click="askBan(item)"
                >
                  <Ban class="size-5" aria-hidden="true" />
                </Button>
              </template>
            </SummaryRow>
          </div>
        </PanelCard>
      </BoardCell>
      <BoardCell v-if="canSeeRules" panel="history" :span="span">
        <HistoryPanel />
      </BoardCell>
      <BoardCell v-if="canSeeRules" panel="thanks" :span="span">
        <ThanksPanel />
      </BoardCell>
    </BoardGrid>
    <!-- The panel's own header carries the close button after the master switch (SidePanel's drawer close);
         h-dvh: a phone's browser bars change the visible height, so the footer must follow it. -->
    <template #panel>
      <SidePanel
        v-if="canSeeRules"
        v-model:open="rulesOpen"
        class="data-[side=right]:h-dvh data-[side=right]:max-h-dvh"
        :label="$t('dashboard.music.rules.title')"
      >
        <RulesPanel flush />
      </SidePanel>
    </template>
  </PanelBeside>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t(`dashboard.music.queue.${confirmKeys.title}`)"
    :description="removeNote"
    :action="$t(`dashboard.music.queue.${confirmKeys.action}`)"
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
