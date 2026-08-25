<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// The TTS AUDIO source: a browser source whose only job is to be the page TTS plays out of. The SDK does
// the playing (it receives the dispatched utterance and plays the server-synthesised audioUrl through a
// media element, which is the only kind of audio OBS captures from a browser source). This widget exists
// so a channel has ONE well-known page for that, instead of every streamer hand-rolling a custom widget.
//
// It renders nothing by default — add it to OBS, size it 1x1, and leave it. "Control audio via OBS" must
// be ON for the scene to hear it. The optional indicator is for setup: turn it on, speak once, confirm the
// source is alive, turn it back off.
interface TtsAudioConfig {
  showIndicator: boolean
  accentColor: string
}

const cfg = reactive<TtsAudioConfig>({
  showIndicator: false,
  accentColor: '#9146ff',
})

const speaking = ref<boolean>(false)
let clearTimer: number | undefined

function onSpeak(payload: any): void {
  if (!cfg.showIndicator) return
  speaking.value = true
  if (clearTimer) window.clearTimeout(clearTimer)
  // Fall back to a short flash when the utterance carries no duration, so the dot cannot stick on.
  const ms: number = Number(payload?.durationMs) > 0 ? Number(payload.durationMs) : 1500
  clearTimer = window.setTimeout(() => (speaking.value = false), ms)
}

function applySettings(next: Partial<TtsAudioConfig> | undefined): void {
  if (!next) return
  Object.assign(cfg, next)
}

onMounted(() => {
  applySettings(nnz?.settings)
  nnz?.onSettings?.(applySettings)
  nnz?.on('tts_speak', onSpeak)
})

onUnmounted(() => {
  nnz?.off('tts_speak', onSpeak)
  if (clearTimer) window.clearTimeout(clearTimer)
})
</script>

<template>
  <div v-if="cfg.showIndicator" class="tts-audio" :class="{ speaking }">
    <span class="dot" :style="{ background: cfg.accentColor }" />
  </div>
</template>

<style scoped>
.tts-audio {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 100%;
}

.dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  opacity: 0.25;
  transition: opacity 120ms linear, transform 120ms linear;
}

.speaking .dot {
  opacity: 1;
  transform: scale(1.35);
}
</style>
