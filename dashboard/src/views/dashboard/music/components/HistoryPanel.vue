<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The latest accepted song requests, newest first. A row rebuilt from the journal has no title yet.
import { useI18n } from 'vue-i18n';

import NoteDouble from '~icons/nnz/note-double';

import { useMusicHistory, type SongHistoryItem } from '@/api/music';
import PanelCard from '@/components/layout/PanelCard.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { locale } = useI18n();
const { channelId } = useCurrentChannel();

const query = useMusicHistory(channelId);
const state = usePanelState(
  query,
  (rows: SongHistoryItem[]): boolean => rows.length === 0,
);

function when(requestedAt: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(requestedAt));
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.music.history.title')"
    :state="state"
    :empty-text="$t('dashboard.music.history.empty')"
    :error-text="$t('dashboard.music.history.error')"
    @retry="query.refetch()"
  >
    <SummaryRow
      v-for="(item, index) in query.data.value"
      :key="`${item.requestedAt}-${index}`"
      :title="item.trackName || $t('dashboard.music.history.unknownTrack')"
      :detail="item.artist || undefined"
      data-history-item
    >
      <template #leading>
        <span
          class="flex size-9 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
          aria-hidden="true"
        >
          <NoteDouble class="size-5" />
        </span>
      </template>
      <template #end>
        <span class="flex flex-none flex-col items-end text-xs text-muted-foreground">
          <UserName
            :user-id="item.requesterId"
            :name="item.requesterName"
            :color="item.requesterColor"
          />
          <span>{{ when(item.requestedAt) }}</span>
        </span>
      </template>
    </SummaryRow>
    <template #footer>
      <span class="text-xs text-muted-foreground" data-history-count>
        {{ $t('dashboard.music.history.showing', { count: query.data.value?.length ?? 0 }) }}
      </span>
    </template>
  </PanelCard>
</template>
