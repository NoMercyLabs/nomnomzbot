<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Media share page, built around the queue viewers fill with clips and videos. The rules for what the bot
// accepts sit in a drawer behind the Settings button, so the queue is the whole page.
import SettingsFader from '~icons/nnz/settings-fader';
import { computed, ref } from 'vue';

import {
  useDecideMediaShare,
  useMediaShareConfig,
  useMediaSharePlayback,
  useMediaSharePlayer,
  useMediaShareQueue,
  useReorderMediaShare,
  type MediaPlaybackCommand,
  type MediaShareRequest,
} from '@/api/mediaShare';
import { useRequiredOverlays } from '@/api/widgets';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import MediaSharePlayer from './components/MediaSharePlayer.vue';
import MediaShareQueueRow from './components/MediaShareQueueRow.vue';
import MediaShareQueueSection from './components/MediaShareQueueSection.vue';
import MediaShareSettings from './components/MediaShareSettings.vue';
import { groupQueue } from './queueSections';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const queueQuery = useMediaShareQueue(channelId);
const configQuery = useMediaShareConfig(channelId);
const decide = useDecideMediaShare(channelId);
const reorder = useReorderMediaShare(channelId);
const playerQuery = useMediaSharePlayer(channelId);
const playback = useMediaSharePlayback(channelId);
const overlaysQuery = useRequiredOverlays(channelId, () =>
  can('widget:read'));

// Without the read (or before it loads) the page cannot tell, so it stays quiet rather than warn falsely.
const overlayLoaded = computed<boolean>(() => {
  const overlays = overlaysQuery.data.value;

  if (!can('widget:read') || overlays === undefined)
    return true;

  return overlays.some((overlay): boolean => overlay.key === 'media_share' && overlay.widget.isAttached === true);
});

const settingsOpen = ref<boolean>(false);
const removing = ref<MediaShareRequest | null>(null);
const confirmOpen = ref<boolean>(false);

const rows = computed<MediaShareRequest[]>(() => queueQuery.data.value ?? []);
const current = computed<MediaShareRequest | null>(() =>
  rows.value.find((row): boolean => row.status === 'playing') ?? null);
const sections = computed(() => groupQueue(rows.value));
const approved = computed<MediaShareRequest[]>(() =>
  rows.value.filter((row): boolean => row.status === 'approved'),
);
const waiting = computed<number>(
  () => rows.value.filter((row): boolean => row.status !== 'played' && row.status !== 'rejected' && row.status !== 'skipped').length,
);

const queueState = usePanelState(
  queueQuery,
  (items: MediaShareRequest[]): boolean => items.length === 0,
);

function position(row: MediaShareRequest): number {
  return approved.value.findIndex((item): boolean => item.id === row.id) + 1;
}

function playPosition(row: MediaShareRequest): number | null {
  return row.status === 'approved'
    ? position(row)
    : null;
}

function move(row: MediaShareRequest, step: number): void {
  reorder.mutate({ id: row.id, position: position(row) + step });
}

function command(name: MediaPlaybackCommand, value?: number): void {
  playback.mutate({ command: name, value });
}

// Play now puts the item first, then the player takes it: a skip ends what plays, a play starts an idle player.
function playNow(row: MediaShareRequest): void {
  reorder.mutate({ id: row.id, position: 1 }, {
    onSuccess: () =>
      command(current.value
        ? 'skip'
        : 'play'),
  });
}

function askRemove(row: MediaShareRequest): void {
  removing.value = row;
  confirmOpen.value = true;
}

function confirmRemove(): void {
  if (removing.value === null) return;

  decide.mutate({ id: removing.value.id, action: 'reject' });
  confirmOpen.value = false;
}
</script>

<template>
  <PageHeader :title="$t('dashboard.mediaShare.title')" :crumb="$t('dashboard.mediaShare.crumb')">
    <template #actions>
      <Button
        v-if="can('media:read')"
        variant="outline"
        data-action="open-settings"
        @click="settingsOpen = true"
      >
        <SettingsFader aria-hidden="true" />
        {{ $t("dashboard.mediaShare.config.title") }}
      </Button>
    </template>
  </PageHeader>
  <PanelBeside>
    <PageBody>
      <MediaSharePlayer
        v-if="can('media:read')"
        :current="current"
        :player="playerQuery.data.value ?? null"
        :has-next="approved.length > 0"
        :can-control="can('pipelines:write')"
        :busy="playback.isPending.value"
        :overlay-loaded="overlayLoaded"
        @command="command"
      />
      <PanelCard
        v-if="can('media:read')"
        :title="$t('dashboard.mediaShare.queue.title')"
        :description="
          waiting > 0
            ? $t('dashboard.mediaShare.queue.waiting', { count: waiting })
            : undefined
        "
        :state="queueState"
        :empty-text="$t('dashboard.mediaShare.queue.empty')"
        :error-text="$t('dashboard.mediaShare.queue.error')"
        @retry="queueQuery.refetch()"
      >
        <MediaShareQueueSection
          v-for="section in sections"
          :key="section.key"
          :kind="section.key"
          :title="$t(`dashboard.mediaShare.queue.section.${section.key}`)"
          :count="section.rows.length"
        >
          <MediaShareQueueRow
            v-for="row in section.rows"
            :key="row.id"
            :row="row"
            :position="playPosition(row)"
            :can-move-up="row.status === 'approved' && position(row) > 1"
            :can-move-down="row.status === 'approved' && position(row) < approved.length"
            @decide="(action) => decide.mutate({ id: row.id, action })"
            @move="(step) => move(row, step)"
            @play-now="playNow(row)"
            @remove="askRemove(row)"
          />
        </MediaShareQueueSection>
      </PanelCard>
    </PageBody>
    <template #panel>
      <SidePanel
        v-if="can('media:read')"
        v-model:open="settingsOpen"
        class="data-[side=right]:h-dvh data-[side=right]:max-h-dvh"
        :label="$t('dashboard.mediaShare.config.title')"
      >
        <MediaShareSettings flush :query="configQuery" :channel-id="channelId" />
      </SidePanel>
    </template>
  </PanelBeside>
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.mediaShare.remove.title')"
    :description="$t('dashboard.mediaShare.remove.description')"
    :action="$t('dashboard.mediaShare.queue.remove')"
    @confirm="confirmRemove"
  >
    <p v-if="removing" class="text-sm font-medium">
      {{ removing.title ?? removing.sourceUrl }}
    </p>
  </ConfirmDialog>
</template>
