<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Speaking indicator + caption for TTS. Driven by the "tts_speak" widget event —
// { text, voice, user, durationMs } (pushable today via the widget_event pipeline action; TTS audio
// itself rides the host page's audio bus via PlaySound, so this widget only renders the caption).
// Idle (hidden) until an event arrives; hides again when the utterance's duration elapses.
interface TtsCaptionConfig {
  showText: boolean
  voiceLabel: boolean   // show the voice name beside the speaker
  position: string      // 'top' | 'bottom'
  accentColor: string
}

const cfg = reactive<TtsCaptionConfig>({
  showText: true,
  voiceLabel: false,
  position: 'bottom',
  accentColor: '#9146ff',
})

const active = ref<boolean>(false)
const text = ref<string>('')
const voice = ref<string>('')
const user = ref<string>('')
let hideTimer: number | undefined

function onSpeak(d: any): void {
  const data: any = d || {}
  text.value = data.text || ''
  voice.value = data.voice || ''
  user.value = data.user || ''
  active.value = true
  if (hideTimer) window.clearTimeout(hideTimer)
  // Fall back to a read-time estimate (~55ms/char, min 3s) when the payload carries no duration.
  const ms: number = isFinite(Number(data.durationMs)) && Number(data.durationMs) > 0
    ? Number(data.durationMs)
    : Math.max(3000, text.value.length * 55)
  hideTimer = window.setTimeout(() => { active.value = false }, ms)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.showText === 'boolean') cfg.showText = s.showText
    if (typeof s.voiceLabel === 'boolean') cfg.voiceLabel = s.voiceLabel
    if (typeof s.position === 'string' && s.position) cfg.position = s.position
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('tts_speak', onSpeak)
})

onUnmounted(() => {
  if (hideTimer) window.clearTimeout(hideTimer)
  if (!nnz) return
  nnz.off('tts_speak', onSpeak)
})
</script>

<template>
  <div
    v-if="active"
    class="nnz-ttscaption"
    :class="'pos-' + cfg.position"
    :style="{ '--accent': cfg.accentColor }"
  >
    <span class="indicator">
      <span class="wave w1"></span><span class="wave w2"></span><span class="wave w3"></span>
    </span>
    <span class="speaker">
      {{ user || 'TTS' }}<span v-if="cfg.voiceLabel && voice" class="voice">({{ voice }})</span>
    </span>
    <span v-if="cfg.showText && text" class="caption">{{ text }}</span>
  </div>
</template>

<style scoped>
.nnz-ttscaption {
  position: fixed;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: 80vw;
  padding: 10px 18px;
  border-radius: 12px;
  color: #fff;
  background: rgba(12, 12, 18, 0.86);
  border: 1px solid var(--accent, #9146ff);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  pointer-events: none;
}
.pos-top {
  top: 24px;
}
.pos-bottom {
  bottom: 24px;
}
.indicator {
  display: inline-flex;
  align-items: flex-end;
  gap: 3px;
  height: 18px;
  flex: none;
}
.wave {
  width: 4px;
  border-radius: 2px;
  background: var(--accent, #9146ff);
  animation: nnz-wave 0.9s ease-in-out infinite;
}
.w1 { height: 8px; }
.w2 { height: 16px; animation-delay: 0.15s; }
.w3 { height: 11px; animation-delay: 0.3s; }
@keyframes nnz-wave {
  0%, 100% { transform: scaleY(0.5); }
  50% { transform: scaleY(1); }
}
.speaker {
  flex: none;
  font-size: 14px;
  font-weight: 700;
  color: var(--accent, #9146ff);
}
.voice {
  margin-left: 4px;
  font-size: 12px;
  font-weight: 500;
  opacity: 0.8;
}
.caption {
  min-width: 0;
  font-size: 16px;
  font-weight: 500;
  line-height: 1.4;
  word-break: break-word;
}
</style>
