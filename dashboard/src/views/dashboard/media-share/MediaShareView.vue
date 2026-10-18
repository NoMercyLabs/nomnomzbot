<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Media share page: the queue viewers fill with clips and videos, and the rules for what the bot accepts.
import { computed } from 'vue';

import {
  useDecideMediaShare,
  useMediaShareConfig,
  useMediaShareQueue,
  useReorderMediaShare,
  type MediaShareRequest,
} from '@/api/mediaShare';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import MediaShareQueueRow from './components/MediaShareQueueRow.vue';
import MediaShareSettings from './components/MediaShareSettings.vue';

const { channelId } = useCurrentChannel();
const { can } = useCan();
const queueQuery = useMediaShareQueue(channelId);
const configQuery = useMediaShareConfig(channelId);
const decide = useDecideMediaShare(channelId);
const reorder = useReorderMediaShare(channelId);

const rows = computed<MediaShareRequest[]>(() => queueQuery.data.value ?? []);
const approved = computed<MediaShareRequest[]>(() =>
  rows.value.filter((row): boolean => row.status === 'approved'),
);

const queueState = usePanelState(
  queueQuery,
  (items: MediaShareRequest[]): boolean => items.length === 0,
);

function position(row: MediaShareRequest): number {
  return approved.value.findIndex((item): boolean => item.id === row.id) + 1;
}

function move(row: MediaShareRequest, step: number): void {
  reorder.mutate({ id: row.id, position: position(row) + step });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.mediaShare.title')" :crumb="$t('dashboard.mediaShare.crumb')" />
  <PageBody>
    <PanelCard
      v-if="can('media:read')"
      tier="lead"
      :title="$t('dashboard.mediaShare.queue.title')"
      :description="
        rows.length > 0
          ? $t('dashboard.mediaShare.queue.waiting', { count: rows.length })
          : undefined
      "
      :state="queueState"
      :empty-text="$t('dashboard.mediaShare.queue.empty')"
      :error-text="$t('dashboard.mediaShare.queue.error')"
      @retry="queueQuery.refetch()"
    >
      <MediaShareQueueRow
        v-for="row in rows"
        :key="row.id"
        :row="row"
        :can-move-up="row.status === 'approved' && position(row) > 1"
        :can-move-down="row.status === 'approved' && position(row) < approved.length"
        @decide="(action) => decide.mutate({ id: row.id, action })"
        @move="(step) => move(row, step)"
      />
    </PanelCard>
    <MediaShareSettings v-if="can('media:read')" :query="configQuery" :channel-id="channelId" />
  </PageBody>
</template>
