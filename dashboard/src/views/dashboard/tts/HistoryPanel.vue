<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What the bot read aloud, newest first. Older rows load a page at a time.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTtsHistory, type TtsHistoryEntry } from '@/api/tts';
import DateTime from '@/components/layout/DateTime.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';

import VoiceLabel from './VoiceLabel.vue';

import ChevronDown from '~icons/nnz/chevron-down';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const history = useTtsHistory(channelId);

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
            v-if="entry.requestedByName"
            :user-id="entry.requestedByUserId"
            :name="entry.requestedByName"
            :color="entry.requestedByColor"
          />
          <VoiceLabel :voice-id="entry.voiceId" />
          <DateTime :value="entry.occurredAt" variant="relative" />
        </p>
      </div>
    </RowGroup>

    <template v-if="history.hasNextPage.value" #footer>
      <Button
        data-slot="history-older"
        type="button"
        variant="outline"
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
