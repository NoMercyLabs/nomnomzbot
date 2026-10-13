<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { Play, Square } from '@lucide/vue'
import { onBeforeUnmount, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useTtsSample } from '@/api/tts'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

// One voice, one button: the first press asks the bot for a sample and plays it, the next press stops it.
const props = defineProps<{ voiceId: string }>()

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const sample = useTtsSample(channelId)
const audio: Ref<HTMLAudioElement | null> = ref(null)
const playing: Ref<boolean> = ref(false)

function stop(): void {
  audio.value?.pause()
  audio.value = null
  playing.value = false
}

async function toggle(): Promise<void> {
  if (playing.value) return stop()
  try {
    const result = await sample.mutateAsync({ voiceId: props.voiceId, text: t('dashboard.tts.voices.sampleText') })
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
    type="button"
    variant="ghost"
    size="icon"
    :disabled="sample.isPending.value"
    :aria-label="$t(playing ? 'dashboard.tts.voices.stopSample' : 'dashboard.tts.voices.playSample', { voice: voiceId })"
    @click="toggle"
  >
    <Square v-if="playing" />
    <Play v-else />
  </Button>
</template>
