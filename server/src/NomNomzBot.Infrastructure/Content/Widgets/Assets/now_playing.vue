<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Standing now-playing display driven by the "music.nowPlaying" widget event (WidgetMusicNowPlayingHandler:
// { engine, title, artists, albumArtUrl, isPlaying, requestedBy, ... }), adapting per the current track's engine:
// Spotify and YouTube playback run on the Audio page; this widget reports the current track visually.
interface NowPlayingConfig {
  layout: string          // 'pill' | 'card'
  showArt: boolean
  showProgressBar: boolean
  provider: string        // '' = show any provider; otherwise only tracks whose payload provider matches
  accentColor: string
}

const cfg = reactive<NowPlayingConfig>({
  layout: 'pill',
  showArt: true,
  showProgressBar: true,
  provider: '',
  accentColor: '#9146ff',
})

const isPlaying = ref<boolean>(false)
const track = ref<string>('')
const artist = ref<string>('')
const artUrl = ref<string>('')
// Who redeemed this track via !sr — empty string (never rendered) when the streamer started it themselves.
const requestedBy = ref<string>('')
// The requester's stored Twitch chat color; empty string falls back to the muted token color.
const requestedByColor = ref<string>('')
const durationMs = ref<number>(0)
const progressMs = ref<number>(0)
const titleEl = ref<HTMLElement | null>(null)
const marqueeEl = ref<HTMLElement | null>(null)

// Heart-pulse: fires once per like/unlike (track_saved_changed), independent of the standing
// now_playing snapshot — a transient animation, not a persisted "is this liked" badge.
const heartPulse = ref<boolean>(false)
const heartIsSaved = ref<boolean>(false)
let heartPulseTimeout: number | undefined

let tickInterval: number | undefined

// The progress bar is INTERPOLATED between pushes, and interpolation is a claim about the world. These two
// anchor it to a measured clock instead of a count of timer callbacks: `progressMs += 100` every 100 ms
// assumes each callback lands exactly on time, and a browser source that OBS has throttled (hidden scene,
// minimised, busy machine) fires them late and sparsely — the bar then runs slow and drifts further behind
// the longer the track plays.
let baseProgressMs = 0
let baseAtMs = 0

const progressPct = computed<number>(() => {
  if (!durationMs.value) return 0
  return Math.min((progressMs.value / durationMs.value) * 100, 100)
})

function startTicking(): void {
  stopTicking()
  baseProgressMs = progressMs.value
  baseAtMs = performance.now()
  tickInterval = window.setInterval(() => {
    const elapsed: number = performance.now() - baseAtMs
    const next: number = baseProgressMs + elapsed

    // Never run past the end of the track. The old ticker counted up forever, so once a track finished the
    // widget kept insisting it was still playing with a full bar until the next push arrived — asserting
    // something it could not know. Stopping at the duration says "this track is done" and waits to be told
    // what is next, rather than inventing it.
    if (durationMs.value > 0 && next >= durationMs.value) {
      progressMs.value = durationMs.value
      stopTicking()
      return
    }

    progressMs.value = next
  }, 100)
}

function stopTicking(): void {
  if (tickInterval) { window.clearInterval(tickInterval); tickInterval = undefined }
}

// Measures real overflow rather than always-on CSS: a title that fits never scrolls, only one that
// genuinely overflows its box gets the marquee class + a --marquee-distance sized to how far it overflows.
function refreshMarquee(): void {
  window.setTimeout(() => {
    if (!titleEl.value || !marqueeEl.value) return
    const el: HTMLElement = marqueeEl.value
    el.classList.remove('animate-marquee')
    el.style.removeProperty('--marquee-distance')
    el.style.removeProperty('--marquee-duration')
    const containerWidth: number = titleEl.value.getBoundingClientRect().width
    const textWidth: number = el.getBoundingClientRect().width
    if (textWidth <= containerWidth) return

    // Scroll only the overflow, at a slow constant speed (about 30 px/s) plus time for the fades and holds.
    const overflow: number = textWidth - containerWidth
    el.style.setProperty('--marquee-distance', `-${overflow}px`)
    el.style.setProperty('--marquee-duration', `${Math.max(6, overflow / 30 + 3)}s`)
    void el.offsetWidth // force a reflow so the animation restarts from the fade-in for the new track
    el.classList.add('animate-marquee')
  }, 300)
}

function onTrackSavedChanged(d: NnzWidgetEventMap['track_saved_changed'] | null | undefined): void {
  const data: Partial<NnzWidgetEventMap['track_saved_changed']> = d || {}
  heartIsSaved.value = !!data.isSaved
  heartPulse.value = true
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  heartPulseTimeout = window.setTimeout(() => { heartPulse.value = false }, 1600)
}

// A dashboard test fire (meta.test) is shown for a few seconds, then the real track is fetched back.
// A real event during the preview ends the preview at once.
const PREVIEW_MS = 8000
let previewTimer: number | undefined

function onNowPlayingEvent(d: NnzWidgetEventMap['music.nowPlaying'] | null | undefined, _type?: string, meta?: NnzEventMeta): void {
  if (previewTimer) { window.clearTimeout(previewTimer); previewTimer = undefined }
  if (meta && meta.test) previewTimer = window.setTimeout(endPreview, PREVIEW_MS)
  onNowPlaying(d)
}

function endPreview(): void {
  previewTimer = undefined
  fetchCurrentState()
}

function onNowPlaying(d: NnzWidgetEventMap['music.nowPlaying'] | null | undefined): void {
  const data: Partial<NnzWidgetEventMap['music.nowPlaying']> = d || {}
  if (cfg.provider && data.engine && data.engine !== cfg.provider) return
  isPlaying.value = !!data.isPlaying
  track.value = data.title || ''
  artist.value = Array.isArray(data.artists) ? data.artists.join(', ') : ''
  artUrl.value = data.albumArtUrl || ''
  requestedBy.value = typeof data.requestedBy === 'string' ? data.requestedBy : ''
  // The shape carries no requester color: the muted token color stands in.
  requestedByColor.value = ''
  durationMs.value = Number.isFinite(Number(data.durationMs)) ? Number(data.durationMs) : 0
  progressMs.value = Number.isFinite(Number(data.progressMs)) ? Number(data.progressMs) : 0
  stopTicking()
  if (isPlaying.value) startTicking()
  refreshMarquee()
}

// Fetch the real current state on mount instead of showing nothing until the next playback change —
// every overlay reload otherwise sat blank until the streamer's next skip/pause/resume.
async function fetchCurrentState(): Promise<void> {
  try {
    const current: NnzWidgetEventMap['music.nowPlaying'] | null = await NomNomz.data.nowPlaying()
    if (current) onNowPlaying(current)
  } catch {
    // Best-effort seed only — the next now_playing hub event still arrives normally.
  }
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  fetchCurrentState()
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.layout === 'string' && s.layout) cfg.layout = s.layout
    if (typeof s.showArt === 'boolean') cfg.showArt = s.showArt
    if (typeof s.showProgressBar === 'boolean') cfg.showProgressBar = s.showProgressBar
    if (typeof s.provider === 'string') cfg.provider = s.provider
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor

  })
  NomNomz.on('music.nowPlaying', onNowPlayingEvent)
  NomNomz.on('track_saved_changed', onTrackSavedChanged)
})

onUnmounted(() => {
  stopTicking()
  if (previewTimer) window.clearTimeout(previewTimer)
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('music.nowPlaying', onNowPlayingEvent)
  NomNomz.off('track_saved_changed', onTrackSavedChanged)
})

</script>

<template>
  <div
    v-if="isPlaying && track"
    class="nnz-nowplaying"
    :class="'layout-' + cfg.layout"
    :style="{ '--accent': cfg.accentColor }"
  >
    <img v-if="cfg.showArt && artUrl" class="art" :src="artUrl" alt="">
    <span v-else class="note">&#9835;</span>
    <div class="meta">
      <div ref="titleEl" class="track">
        <span ref="marqueeEl" class="track-text">{{ track }}</span>
      </div>
      <div v-if="artist" class="artist">{{ artist }}</div>
      <div v-if="requestedBy" class="requester">
        requested by
        <span class="requester-name" :style="requestedByColor ? { color: requestedByColor } : undefined">{{ requestedBy }}</span>
      </div>
      <div v-if="cfg.showProgressBar" class="bar"><div class="fill" :style="{ width: progressPct + '%' }"></div></div>
    </div>
    <span
      v-if="heartPulse"
      class="heart"
      :class="{ 'heart-saved': heartIsSaved, 'heart-unsaved': !heartIsSaved }"
    >&#10084;</span>
  </div>

</template>

<style scoped>
.nnz-nowplaying {
  position: fixed;
  left: 16px;
  bottom: 16px;
  display: flex;
  align-items: center;
  gap: 10px;
  max-width: 46vw;
  color: #fff;
  background: rgba(12, 12, 18, 0.85);
  border: 1px solid var(--accent, #9146ff);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
.layout-pill {
  padding: 8px 16px;
  border-radius: 999px;
}
.layout-card {
  padding: 12px 16px;
  border-radius: 12px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
}
.note {
  color: var(--accent, #9146ff);
  font-size: 18px;
}
.art {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  object-fit: cover;
  flex: none;
}
.layout-pill .art {
  width: 26px;
  height: 26px;
  border-radius: 50%;
}
.meta {
  min-width: 0;
}
.track {
  font-size: 15px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
}
.track-text {
  display: inline-block;
}
.track-text.animate-marquee {
  animation: nnz-marquee var(--marquee-duration, 8s) linear infinite;
}
/* Cycle: fade in, hold, slow linear move to the end of the line, hold, fade out, reset while invisible, replay. */
@keyframes nnz-marquee {
  0% { transform: translateX(0); opacity: 0; }
  8% { transform: translateX(0); opacity: 1; }
  18% { transform: translateX(0); opacity: 1; }
  80% { transform: translateX(var(--marquee-distance, 0)); opacity: 1; }
  90% { transform: translateX(var(--marquee-distance, 0)); opacity: 0; }
  100% { transform: translateX(var(--marquee-distance, 0)); opacity: 0; }
}
.artist {
  font-size: 12px;
  opacity: 0.75;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.requester {
  font-size: 11px;
  opacity: 0.6;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.requester-name {
  font-weight: 600;
}
.bar {
  margin-top: 6px;
  height: 3px;
  border-radius: 2px;
  overflow: hidden;
  background: rgba(255, 255, 255, 0.15);
}
.layout-pill .bar {
  display: none; /* the pill stays compact; the progress fill is a card-layout detail */
}
.fill {
  height: 100%;
  border-radius: 2px;
  background: var(--accent, #9146ff);
  transition: width 0.3s linear;
}
.heart {
  position: absolute;
  top: -10px;
  right: -8px;
  font-size: 20px;
  line-height: 1;
  pointer-events: none;
  animation: nnz-heart-pulse 1.6s ease-out forwards;
}
.heart-saved { color: #ff4d6d; }
.heart-unsaved {
  color: #8a8a94;
  text-decoration: line-through;
}
@keyframes nnz-heart-pulse {
  0% { transform: scale(0.3); opacity: 0; }
  20% { transform: scale(1.4); opacity: 1; }
  35% { transform: scale(1); opacity: 1; }
  75% { transform: scale(1); opacity: 1; }
  100% { transform: scale(0.8) translateY(-14px); opacity: 0; }
}
</style>
