<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Emotes from chat float across the screen. Binds the decorated "ChatMessage" overlay event and
// harvests its emote/cheermote fragments (resolved image urls — Twitch plus the BTTV/FFZ/7TV fragments
// once the chat-decoration subsystem ships them). Idle until emotes arrive.
interface EmoteWallConfig {
  density: number      // max emotes on screen at once
  size: number         // base emote size in px
  animation: string    // 'float' (drift up) | 'rain' (fall down)
  providers: string[]  // filter by fragment provider when it is present; [] = all
  accentColor: string
}

const cfg = reactive<EmoteWallConfig>({
  density: 30,
  size: 48,
  animation: 'float',
  providers: [],
  accentColor: '#9146ff',
})

interface FlyingEmote {
  id: number
  url: string
  left: number       // vw
  size: number       // px
  durationMs: number
  drift: number      // px of horizontal sway
}

const emotes = ref<FlyingEmote[]>([])
let seq = 0
const removeTimers: number[] = []

// The decorated "ChatMessage" event is not in the typed event map, so its payload arrives as `unknown` and
// only the parts this widget reads are described here.
interface FragmentUrls { [size: string]: string | undefined }
interface ChatFragment {
  type?: string
  emote?: { provider?: string; urls?: FragmentUrls }
  cheermote?: { urls?: FragmentUrls }
}

function firstUrl(urls: FragmentUrls | undefined, keys: string[]): string {
  if (!urls) return ''
  for (const key of keys) {
    const url: string | undefined = urls[key]
    if (url) return url
  }
  return ''
}

function providerAllowed(fr: ChatFragment): boolean {
  if (!cfg.providers.length) return true
  const p: string = ((fr.emote && fr.emote.provider) || 'twitch').toLowerCase()
  return cfg.providers.indexOf(p) !== -1
}

function spawn(url: string): void {
  if (emotes.value.length >= Math.max(1, cfg.density)) return
  const size: number = Math.round(cfg.size * (0.75 + Math.random() * 0.5))
  const durationMs: number = 4000 + Math.round(Math.random() * 3000)
  const fly: FlyingEmote = {
    id: ++seq,
    url: url,
    left: Math.round(Math.random() * 92) + 2,
    size: size,
    durationMs: durationMs,
    drift: Math.round((Math.random() - 0.5) * 120),
  }
  emotes.value = emotes.value.concat([fly])
  removeTimers.push(window.setTimeout(() => {
    emotes.value = emotes.value.filter((e: FlyingEmote) => e.id !== fly.id)
  }, durationMs))
}

function onChat(m: unknown): void {
  const message: { fragments?: unknown } | null = (typeof m === 'object' && m !== null) ? (m as { fragments?: unknown }) : null
  const fragments: ChatFragment[] = (message && Array.isArray(message.fragments)) ? message.fragments : []
  fragments.forEach((fr: ChatFragment | null) => {
    if (!fr) return
    let url = ''
    if (fr.type === 'emote' && fr.emote && providerAllowed(fr))
      url = firstUrl(fr.emote.urls, ['2', '3', '1'])
    else if (fr.type === 'cheermote' && fr.cheermote)
      url = firstUrl(fr.cheermote.urls, ['2', '3', '1'])
    if (url) spawn(url)
  })
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.density)) && Number(s.density) > 0) cfg.density = Number(s.density)
    if (isFinite(Number(s.size)) && Number(s.size) > 0) cfg.size = Number(s.size)
    if (typeof s.animation === 'string' && s.animation) cfg.animation = s.animation
    if (Array.isArray(s.providers)) cfg.providers = s.providers.map((p: string) => String(p).toLowerCase())
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('ChatMessage', onChat)
})

onUnmounted(() => {
  removeTimers.forEach((t: number) => window.clearTimeout(t))
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('ChatMessage', onChat)
})
</script>

<template>
  <div class="nnz-emotewall" :class="'anim-' + cfg.animation">
    <img
      v-for="e in emotes"
      :key="e.id"
      class="emote"
      :src="e.url"
      alt=""
      :style="{
        left: e.left + 'vw',
        width: e.size + 'px',
        animationDuration: e.durationMs + 'ms',
        '--drift': e.drift + 'px',
      }"
    >
  </div>
</template>

<style scoped>
.nnz-emotewall {
  position: fixed;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
}
.emote {
  position: absolute;
  height: auto;
  will-change: transform, opacity;
  animation-timing-function: linear;
  animation-fill-mode: forwards;
}
.anim-float .emote {
  bottom: -80px;
  animation-name: nnz-float;
}
.anim-rain .emote {
  top: -80px;
  animation-name: nnz-rain;
}
@keyframes nnz-float {
  0% { transform: translate(0, 0) rotate(-6deg); opacity: 0; }
  10% { opacity: 1; }
  85% { opacity: 1; }
  100% { transform: translate(var(--drift, 0px), calc(-100vh - 160px)) rotate(6deg); opacity: 0; }
}
@keyframes nnz-rain {
  0% { transform: translate(0, 0) rotate(6deg); opacity: 0; }
  10% { opacity: 1; }
  85% { opacity: 1; }
  100% { transform: translate(var(--drift, 0px), calc(100vh + 160px)) rotate(-6deg); opacity: 0; }
}
</style>
