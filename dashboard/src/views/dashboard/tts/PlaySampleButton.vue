<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import LoaderFade from '~icons/nnz/loader-fade'
import Play from '~icons/nnz/play'
import Square from '~icons/nnz/stop'
import { computed, onBeforeUnmount, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useTtsSample } from '@/api/tts'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

type SampleState = 'idle' | 'loading' | 'playing'

// One voice, one button: the first press asks the bot for a sample and plays it, the next press stops it.
// compact hides the words and keeps them as the accessible name.
const props = withDefaults(defineProps<{ voiceId: string; compact?: boolean }>(), {
  compact: false,
})

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const sample = useTtsSample(channelId)
const audio: Ref<HTMLAudioElement | null> = ref(null)
const playing: Ref<boolean> = ref(false)

const state: ComputedRef<SampleState> = computed<SampleState>(() =>
  playing.value ? 'playing' : sample.isPending.value ? 'loading' : 'idle',
)

function stop(): void {
  audio.value?.pause()
  audio.value = null
  playing.value = false
}

async function toggle(): Promise<void> {
  if (playing.value) return stop()
  try {
    const result = await sample.mutateAsync({
      voiceId: props.voiceId,
      text: t('dashboard.tts.voices.sampleText'),
    })
    const player: HTMLAudioElement = new Audio(`data:audio/mpeg;base64,${result.audioBase64}`)
    player.addEventListener('ended', stop)
    audio.value = player
    playing.value = true
    await player.play()
  } catch {
    stop()
  }
}

onBeforeUnmount(stop)
</script>

<template>
  <Button
    data-voice-play
    :data-state="state"
    type="button"
    variant="outline"
    :size="compact ? 'icon' : 'sm'"
    :disabled="state === 'loading'"
    :aria-busy="state === 'loading'"
    :aria-label="
      $t(
        state === 'playing' ? 'dashboard.tts.voices.stopSample' : 'dashboard.tts.voices.playSample',
        {
          voice: voiceId,
        },
      )
    "
    @click="toggle"
  >
    <LoaderFade v-if="state === 'loading'" :class="['animate-spin', { 'size-5': compact }]" />
    <Square v-else-if="state === 'playing'" :class="{ 'size-5': compact }" />
    <Play v-else :class="{ 'size-5': compact }" />
    <template v-if="!compact">
      {{
        $t(
          state === 'playing' ? 'dashboard.tts.voices.stopLabel' : 'dashboard.tts.voices.playLabel',
        )
      }}
    </template>
  </Button>
</template>
