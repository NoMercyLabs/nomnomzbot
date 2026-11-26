<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
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
  animation: string    // float | rain | pop | burst | bounce | wave (see the keyframes below)
  providers: string[]  // filter by fragment provider when it is present; [] = all
}

const cfg = reactive<EmoteWallConfig>({
  density: 30,
  size: 48,
  animation: 'float',
  providers: [],
})

interface FlyingEmote {
  id: number
  url: string
  left: number       // vw
  top: number        // vh, where pop and burst place it
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
    top: Math.round(Math.random() * 80) + 5,
    size: size,
    durationMs: durationMs,
    // burst throws each emote out from the middle, so its sway is much wider.
    drift: Math.round((Math.random() - 0.5) * (cfg.animation === 'burst' ? 900 : 120)),
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
  <!-- The emotes are the art; the theme only sets the root type. Placement and flight stay here. -->
  <div class="nnz-widget nnz-emotewall" :class="'anim-' + cfg.animation">
    <img
      v-for="e in emotes"
      :key="e.id"
      class="emote"
      :src="e.url"
      alt=""
      :style="{
        left: e.left + 'vw',
        '--top': e.top + 'vh',
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
/* pop: appears in place, swells, then fades. */
.anim-pop .emote {
  top: var(--top);
  animation-name: nnz-pop;
  animation-timing-function: cubic-bezier(0.2, 0.9, 0.3, 1.4);
}
/* burst: thrown out from the middle of the screen. */
.anim-burst .emote {
  top: calc(50 * var(--nnz-vh, 1vh));
  left: calc(50 * var(--nnz-vw, 1vw)) !important;
  animation-name: nnz-burst;
  animation-timing-function: cubic-bezier(0.1, 0.7, 0.3, 1);
}
/* bounce: drops in and bounces on the bottom edge. */
.anim-bounce .emote {
  top: -80px;
  animation-name: nnz-bounce;
}
/* wave: swims across the screen from left to right in a wave. */
.anim-wave .emote {
  top: var(--top);
  left: -80px !important;
  animation-name: nnz-wave;
}
@keyframes nnz-pop {
  0% { transform: scale(0); opacity: 0; }
  15% { transform: scale(1.3); opacity: 1; }
  25% { transform: scale(1); }
  80% { transform: scale(1); opacity: 1; }
  100% { transform: scale(0.6); opacity: 0; }
}
@keyframes nnz-burst {
  0% { transform: translate(0, 0) scale(0.2) rotate(0deg); opacity: 0; }
  10% { opacity: 1; }
  80% { opacity: 1; }
  100% { transform: translate(var(--drift, 0px), calc(var(--drift, 0px) * -0.6)) scale(1.2) rotate(360deg); opacity: 0; }
}
@keyframes nnz-bounce {
  0% { transform: translateY(0); opacity: 1; animation-timing-function: ease-in; }
  35% { transform: translateY(calc(100 * var(--nnz-vh, 1vh) - 20px - 100%)); animation-timing-function: ease-out; }
  50% { transform: translateY(calc(70 * var(--nnz-vh, 1vh) - 100%)); animation-timing-function: ease-in; }
  65% { transform: translateY(calc(100 * var(--nnz-vh, 1vh) - 20px - 100%)); animation-timing-function: ease-out; }
  75% { transform: translateY(calc(88 * var(--nnz-vh, 1vh) - 100%)); animation-timing-function: ease-in; }
  85% { transform: translateY(calc(100 * var(--nnz-vh, 1vh) - 20px - 100%)); opacity: 1; }
  100% { transform: translateY(calc(100 * var(--nnz-vh, 1vh) - 20px - 100%)); opacity: 0; }
}
@keyframes nnz-wave {
  0% { transform: translate(0, 0); }
  25% { transform: translate(calc(27 * var(--nnz-vw, 1vw)), -40px); }
  50% { transform: translate(calc(54 * var(--nnz-vw, 1vw)), 0); }
  75% { transform: translate(calc(81 * var(--nnz-vw, 1vw)), 40px); }
  100% { transform: translate(calc(100 * var(--nnz-vw, 1vw) + 160px), 0); }
}
@keyframes nnz-float {
  0% { transform: translate(0, 0) rotate(-6deg); opacity: 0; }
  10% { opacity: 1; }
  85% { opacity: 1; }
  100% { transform: translate(var(--drift, 0px), calc(-100 * var(--nnz-vh, 1vh) - 160px)) rotate(6deg); opacity: 0; }
}
@keyframes nnz-rain {
  0% { transform: translate(0, 0) rotate(6deg); opacity: 0; }
  10% { opacity: 1; }
  85% { opacity: 1; }
  100% { transform: translate(var(--drift, 0px), calc(100 * var(--nnz-vh, 1vh) + 160px)) rotate(-6deg); opacity: 0; }
}
</style>
