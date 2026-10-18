<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Picks the voice for one person from the channel's voice catalog, or puts them back on the channel voice.
// The search asks the bot, so a catalog of thousands stays one short list.
import { refDebounced } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import Check from '~icons/nnz/check';

import { useSetPersonVoice, useTtsVoices, type TtsVoice } from '@/api/tts';
import FormDialog from '@/components/layout/FormDialog.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import PlaySampleButton from '@/views/dashboard/tts/PlaySampleButton.vue';
import { localeLabel } from '@/views/dashboard/tts/voiceGroups';

const SEARCH_DELAY_MS = 250;

const props = defineProps<{
  userId: string;
  name: string;
  voiceId: string | null;
}>();
const open = defineModel<boolean>('open', { required: true });

const { locale } = useI18n();
const { channelId } = useCurrentChannel();
const setVoice = useSetPersonVoice(channelId);

const search = ref('');
const asked = refDebounced(search, SEARCH_DELAY_MS);
// null is the channel voice.
const chosen = ref<TtsVoice | null>(null);
const chosenId = ref<string | null>(null);

const catalog = useTtsVoices(
  () =>
    open.value
      ? channelId.value
      : null,
  () => ({ q: asked.value }),
);
const voices = computed(() =>
  (catalog.data.value?.pages ?? []).flatMap((page) =>
    page.voices));

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  search.value = '';
  chosen.value = null;
  chosenId.value = props.voiceId;
});

function pick(voice: TtsVoice | null): void {
  chosen.value = voice;
  chosenId.value = voice?.id ?? null;
}

function save(): void {
  if (chosenId.value !== props.voiceId) {
    setVoice.mutate({ userId: props.userId, voice: chosen.value });
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-profile-voice-dialog
    :title="$t('profile.voice.editTitle', { name })"
    :description="$t('profile.voice.editDescription')"
    :action="$t('profile.voice.save')"
    @submit="save"
  >
    <Input
      v-model="search"
      data-profile-voice-search
      type="search"
      autofocus
      :aria-label="$t('profile.voice.search')"
      :placeholder="$t('profile.voice.search')"
    />
    <RowGroup role="radiogroup" :aria-label="$t('profile.voice.title')">
      <SummaryRow
        data-profile-voice-option="channel"
        pick
        role="radio"
        :aria-checked="chosenId === null"
        :selected="chosenId === null"
        :title="$t('profile.voice.channel')"
        :detail="$t('profile.voice.channelDetail')"
        @click="pick(null)"
      >
        <template #leading>
          <Check class="size-4 flex-none" :class="chosenId === null ? '' : 'invisible'" aria-hidden="true" />
        </template>
      </SummaryRow>
      <SummaryRow
        v-for="voice in voices"
        :key="voice.id"
        :data-profile-voice-option="voice.id"
        pick
        role="radio"
        :aria-checked="chosenId === voice.id"
        :selected="chosenId === voice.id"
        :title="voice.displayName"
        :detail="localeLabel(voice.locale, locale)"
        @click="pick(voice)"
      >
        <template #leading>
          <Check
            class="size-4 flex-none"
            :class="chosenId === voice.id ? '' : 'invisible'"
            aria-hidden="true"
          />
        </template>
        <template #trailing><PlaySampleButton :voice-id="voice.id" compact /></template>
      </SummaryRow>
    </RowGroup>
    <Button
      v-if="catalog.hasNextPage.value"
      data-profile-voice-more
      type="button"
      variant="ghost"
      size="sm"
      :disabled="catalog.isFetchingNextPage.value"
      @click="catalog.fetchNextPage()"
    >
      {{ $t('profile.voice.more') }}
    </Button>
  </FormDialog>
</template>
