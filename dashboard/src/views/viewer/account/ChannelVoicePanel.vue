<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The person's own TTS voice in one channel. A pick changes only their own voice, read aloud only for their own
// words; the channel voice and every other viewer stay as they are.
import { computed, ref } from 'vue';

import { useTtsVoices, type TtsVoice, type TtsVoicePage } from '@/api/tts';
import { useMyVoice, useSetMyVoice } from '@/api/viewerAccount';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { merge } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';

const props = defineProps<{ channelId: string | null }>();

function voicesOf(page: TtsVoicePage): TtsVoice[] {
  return page.voices;
}

const query = ref('');
const mine = useMyVoice(() => props.channelId);
const save = useSetMyVoice(() => props.channelId);
const catalog = useTtsVoices(() => props.channelId, () => ({ q: query.value }));

const pages = computed<readonly TtsVoicePage[]>(() => catalog.data.value?.pages ?? []);
const voiceId = computed<string | null>(() => mine.data.value?.voiceId ?? null);
// The voice in use leads the list so it never hides below the search results.
const voices = computed<readonly TtsVoice[]>(() => {
  const all = pages.value.flatMap(voicesOf);

  return merge(
    all.filter((voice) => voice.id === voiceId.value),
    all.filter((voice) => voice.id !== voiceId.value),
  );
});
const state = usePanelState(
  {
    data: computed(() => catalog.data.value?.pages),
    isPending: catalog.isPending,
    isError: catalog.isError,
  },
  () => voices.value.length === 0,
);

function use(voice: TtsVoice): void {
  save.mutate(voice.id);
}
</script>

<template>
  <PanelCard
    :title="$t('viewer.account.voice.title')"
    :description="$t('viewer.account.voice.description')"
    :state="state"
    :empty-text="$t('viewer.account.voice.empty')"
    :error-text="$t('viewer.account.voice.closed')"
    data-viewer-voice
    @retry="catalog.refetch()"
  >
    <template #actions>
      <Button
        v-if="voiceId !== null"
        variant="outline"
        size="sm"
        data-voice-reset
        @click="save.mutate(null)"
      >
        {{ $t('viewer.account.voice.reset') }}
      </Button>
    </template>
    <div class="flex flex-col gap-3">
      <SearchField v-model="query" :placeholder="$t('viewer.account.voice.search')" />
      <p v-if="voiceId === null" class="text-sm text-muted-foreground" data-channel-voice>
        {{ $t('viewer.account.voice.channelVoice') }}
      </p>
      <RowGroup>
        <SummaryRow
          v-for="voice in voices"
          :key="voice.id"
          :title="voice.displayName"
          :detail="voice.locale"
          :selected="voice.id === voiceId"
          data-voice-row
        >
          <template #end>
            <Badge v-if="voice.id === voiceId" variant="secondary" class="mr-3">
              {{ $t('viewer.account.voice.inUse') }}
            </Badge>
            <Button v-else
                    variant="ghost"
                    size="sm"
                    data-voice-use
                    @click="use(voice)"
            >
              {{ $t('viewer.account.voice.use') }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </div>
  </PanelCard>
</template>
