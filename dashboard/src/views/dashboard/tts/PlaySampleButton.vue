<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import LoaderFade from '~icons/nnz/loader-fade';
import Play from '~icons/nnz/play';
import Square from '~icons/nnz/stop';
import { computed, onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTtsSample } from '@/api/tts';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

type SampleState = 'idle' | 'loading' | 'playing';

// One voice, one button: the first press asks the bot for a sample and plays it, the next press stops it.
// compact hides the words and keeps them as the accessible name. text makes the bot speak that line
// (a pronunciation rule's phrase) instead of the voice's own introduction.
const props = withDefaults(
  defineProps<{
    voiceId: string;
    compact?: boolean;
    text?: string;
    label?: string;
  }>(),
  { compact: false },
);

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const sample = useTtsSample(channelId);
const audio = ref<HTMLAudioElement | null>(null);
const playing = ref(false);

const state = computed<SampleState>(() => {
  if (playing.value)
    return 'playing';

  if (sample.isPending.value)
    return 'loading';

  return 'idle';
});

const name = computed<string>(() => {
  if (state.value === 'playing')
    return t('dashboard.tts.voices.stopSample', { voice: props.voiceId });

  if (props.label !== undefined)
    return props.label;

  return props.text === undefined
    ? t('dashboard.tts.voices.playSample', { voice: props.voiceId })
    : t('dashboard.tts.lexicon.hear', { phrase: props.text });
});

const visibleLabel = computed<string>(() => {
  if (state.value === 'playing')
    return t('dashboard.tts.voices.stopLabel');

  return props.label ?? t('dashboard.tts.voices.playLabel');
});

function stop(): void {
  audio.value?.pause();
  audio.value = null;
  playing.value = false;
}

async function toggle(): Promise<void> {
  if (playing.value)
    return stop();

  try {
    const result = await sample.mutateAsync(
      props.text === undefined
        ? { voiceId: props.voiceId }
        : {
            voiceId: props.voiceId,
            text: props.text,
          },
    );
    const player = new Audio(`data:audio/mpeg;base64,${result.audioBase64}`);

    player.addEventListener('ended', stop);
    audio.value = player;
    playing.value = true;
    await player.play();
  } catch {
    stop();
  }
}

onBeforeUnmount(stop);
</script>

<template>
  <Button
    v-if="can('tts:voice:test')"
    data-voice-play
    :data-state="state"
    type="button"
    variant="outline"
    :size="compact ? 'icon' : 'sm'"
    :disabled="state === 'loading'"
    :aria-busy="state === 'loading'"
    :aria-label="name"
    @click="toggle"
  >
    <LoaderFade v-if="state === 'loading'" :class="['animate-spin', { 'size-5': compact }]" />
    <Square v-else-if="state === 'playing'" :class="{ 'size-5': compact }" />
    <Play v-else :class="{ 'size-5': compact }" />
    <template v-if="!compact">
      {{ visibleLabel }}
    </template>
  </Button>
</template>
