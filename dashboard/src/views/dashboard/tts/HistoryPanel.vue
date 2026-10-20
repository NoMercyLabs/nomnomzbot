<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What the bot read aloud, newest first. Older rows load a page at a time.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTtsHistory, useTtsVoices, type TtsHistoryEntry } from '@/api/tts';
import DateTime from '@/components/layout/DateTime.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';

import ChevronDown from '~icons/nnz/chevron-down';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const history = useTtsHistory(channelId);
// The same first page the Voices tab loads, so a voice reads by its name here too.
const catalog = useTtsVoices(channelId, () => ({ q: '' }));

const entries = computed<TtsHistoryEntry[]>(() =>
  (history.data.value?.pages ?? []).flatMap((page) => page.items));

const state = computed<PanelState>(() => {
  if (history.isError.value)
    return 'error';

  if (history.isPending.value)
    return 'loading';

  return entries.value.length === 0
    ? 'empty'
    : 'ready';
});

function voiceName(voiceId: string): string {
  const loaded = (catalog.data.value?.pages ?? []).flatMap((page) => page.voices);

  return loaded.find((voice) => voice.id === voiceId)?.displayName ?? voiceId;
}
</script>

<template>
  <PanelCard
    data-view="tts-history"
    tier="lead"
    :title="$t('dashboard.tts.history.title')"
    :description="$t('dashboard.tts.history.description')"
    :state="state"
    :empty-text="$t('dashboard.tts.history.empty')"
    :error-text="$t('dashboard.tts.history.error')"
    @retry="history.refetch()"
  >
    <RowGroup>
      <div
        v-for="entry in entries"
        :key="entry.id"
        data-history-row
        class="min-w-0 rounded-lg px-3 py-2"
      >
        <p
          data-history-text
          class="text-sm wrap-break-word"
          :class="entry.text ? '' : 'text-muted-foreground'"
        >
          {{ entry.text || t('dashboard.tts.history.noText') }}
        </p>
        <p class="flex flex-wrap items-center gap-x-3 text-sm text-muted-foreground">
          <UserName
            :user-id="entry.requestedByUserId"
            :name="entry.requestedByName || t('dashboard.tts.history.unknownViewer')"
            :color="entry.requestedByColor"
          />
          <span>{{ voiceName(entry.voiceId) }}</span>
          <DateTime :value="entry.occurredAt" variant="relative" />
        </p>
      </div>
    </RowGroup>

    <template v-if="history.hasNextPage.value" #footer>
      <Button
        data-slot="history-older"
        type="button"
        variant="ghost"
        size="sm"
        class="w-full"
        :disabled="history.isFetchingNextPage.value"
        @click="history.fetchNextPage()"
      >
        {{ t('dashboard.tts.history.older') }}
        <ChevronDown aria-hidden="true" />
      </Button>
    </template>
  </PanelCard>
</template>
