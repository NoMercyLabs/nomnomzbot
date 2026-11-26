<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted, type ComponentPublicInstance } from 'vue'

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
  anchor: string
}

const cfg = reactive<NowPlayingConfig>({
  layout: 'pill',
  showArt: true,
  showProgressBar: true,
  provider: '',
  accentColor: '#9146ff',
  anchor: 'bottom-left',
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

// The cover is a square as tall as the text block, so its gap to the box edge is the same on top, bottom and left.
// CSS stretches its height; a stretched height cannot set the width, so the observer copies it over. The copy
// waits a frame: a resize inside the observer callback is a "ResizeObserver loop" error in the page.
const artSize = ref<number>(0)
const artObserver: ResizeObserver = new ResizeObserver((entries: ResizeObserverEntry[]) => {
  const size: number = entries[0]?.borderBoxSize[0]?.blockSize ?? 0
  requestAnimationFrame(() => { artSize.value = size })
})

function trackArt(el: Element | ComponentPublicInstance | null): void {
  artObserver.disconnect()
  if (el instanceof HTMLElement) artObserver.observe(el)
}

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
    // Layout widths, not on-screen ones: translateX works before the theme's zoom, so a zoomed measure overshoots.
    const textWidth: number = el.offsetWidth
    const containerWidth: number = titleEl.value.clientWidth
    const overflow: number = textWidth - containerWidth
    if (overflow <= 0) return

    // The move takes 52% of the cycle (18% to 70%) at about 30 px/s; the rest is the fades and the two holds.
    el.style.setProperty('--marquee-distance', `-${overflow}px`)
    el.style.setProperty('--marquee-duration', `${Math.max(8, overflow / 30 / 0.52)}s`)
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
    if (typeof s.anchor === 'string' && s.anchor) cfg.anchor = s.anchor

  })
  NomNomz.on('music.nowPlaying', onNowPlayingEvent)
  NomNomz.on('track_saved_changed', onTrackSavedChanged)
})

onUnmounted(() => {
  stopTicking()
  artObserver.disconnect()
  if (previewTimer) window.clearTimeout(previewTimer)
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('music.nowPlaying', onNowPlayingEvent)
  NomNomz.off('track_saved_changed', onTrackSavedChanged)
})

</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the widget. -->
  <Transition name="nnz">
    <div v-if="isPlaying && track" class="nnz-widget nnz-placed nnz-nowplaying" :class="'layout-' + cfg.layout" :data-anchor="cfg.anchor">
      <div class="box" :class="cfg.layout === 'card' ? 'nnz-card' : 'nnz-pill'">
        <!-- The cover frame keeps its size with or without a picture: a missing or broken cover shows the note in it. -->
        <span v-if="cfg.showArt" :ref="trackArt" class="nnz-avatar art" :style="{ width: artSize + 'px' }">
          <img v-if="artUrl" class="art-img" :src="artUrl" alt="" @error="artUrl = ''">
          <span v-else class="note nnz-accent-text">&#9835;</span>
        </span>
        <span v-else class="note nnz-accent-text">&#9835;</span>
        <!-- The sizer holds the height of every row, so the box never changes size between songs; the shown rows
             spread evenly over that height, so a song with no requester leaves no hole. -->
        <div class="meta">
          <div class="sizer" aria-hidden="true">
            <div class="track">&nbsp;</div>
            <div class="nnz-sub artist">&nbsp;</div>
            <div class="nnz-sub requester">&nbsp;</div>
            <div v-if="cfg.showProgressBar" class="nnz-track bar"></div>
          </div>
          <div class="content">
          <div ref="titleEl" class="track">
            <span ref="marqueeEl" class="track-text">{{ track }}</span>
          </div>
          <div class="nnz-sub artist">{{ artist || ' ' }}</div>
          <div v-if="requestedBy" class="nnz-sub requester">
            Requested by
            <span class="requester-name" :style="requestedByColor ? { color: requestedByColor } : undefined">{{ requestedBy || ' ' }}</span>
          </div>
          <div v-if="cfg.showProgressBar" class="nnz-track bar"><div class="nnz-fill" :style="{ width: progressPct + '%' }"></div></div>
          </div>
        </div>
      </div>
      <span
        v-if="heartPulse"
        class="nnz-num heart"
        :class="{ 'heart-saved': heartIsSaved, 'heart-unsaved': !heartIsSaved }"
      >&#10084;</span>
    </div>
  </Transition>

</template>

<style scoped>
/* The box keeps one width whatever the title length, so it never jumps between songs; the anchor puts it in its corner. */
.nnz-placed.nnz-nowplaying {
  /* The zoomed box inside carries the shadow, so its reach is zoomed too; each side keeps its own margin. */
  --reach: calc(var(--nnz-shadow-reach) * var(--nnz-z));
  --nnz-inset: calc(var(--nnz-edge) + var(--reach));
  --nnz-inset-top: calc(var(--nnz-mt) + var(--reach));
  --nnz-inset-right: calc(var(--nnz-mr) + var(--reach));
  --nnz-inset-bottom: calc(var(--nnz-mb) + var(--reach));
  --nnz-inset-left: calc(var(--nnz-ml) + var(--reach));
  width: min(calc(100% - var(--nnz-ml) - var(--nnz-mr) - 2 * var(--reach)), 616px);
}
.box {
  display: flex;
  align-items: stretch;
  gap: var(--nnz-gap);
  /* No width: an auto width fills the slot in every Chromium zoom model; a percentage on a zoomed box does not. */
  box-sizing: border-box;
}
.meta {
  flex: 1;
  align-self: center;
}
.note {
  flex: none;
  align-self: center;
  font-size: 20px;
}
/* The cover stretches to the text block's height; size containment keeps the picture's own size out of that height.
   The left padding matches the top and bottom padding, and the corner sits concentric inside the box's. */
.art {
  align-self: stretch;
  height: auto;
  contain: size;
  display: grid;
  place-items: center;
  overflow: hidden;
}
.art-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}
.art .note {
  font-size: 28px;
}
.layout-pill .box {
  padding-left: 10px;
}
.layout-pill .art {
  border-radius: max(4px, calc(var(--nnz-radius-pill) - 10px));
}
.layout-card .box {
  padding-left: 12px;
}
.layout-card .art {
  border-radius: max(4px, calc(var(--nnz-radius) - 12px));
}
.meta {
  min-width: 0;
  display: grid;
}
.sizer,
.content {
  grid-area: 1 / 1;
  min-width: 0;
}
.sizer {
  visibility: hidden;
}
.content {
  display: flex;
  flex-direction: column;
  justify-content: space-evenly;
}
.track {
  font-size: 16px;
  font-weight: 800;
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
  70% { transform: translateX(var(--marquee-distance, 0)); opacity: 1; }
  92% { transform: translateX(var(--marquee-distance, 0)); opacity: 1; }
  98% { transform: translateX(var(--marquee-distance, 0)); opacity: 0; }
  100% { transform: translateX(var(--marquee-distance, 0)); opacity: 0; }
}
.artist {
  font-size: 13px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.requester {
  font-size: 12px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.requester-name {
  font-weight: 800;
}
.bar {
  margin-top: 6px;
  height: 8px;
}
.layout-pill .bar {
  margin-top: 4px;
  height: 5px;
}
.heart {
  position: absolute;
  top: -12px;
  right: -10px;
  font-size: 16px;
  line-height: 1;
  pointer-events: none;
  background: var(--nnz-surface);
  box-shadow: var(--nnz-shadow);
  animation: nnz-heart-pulse 1.6s ease-out forwards;
}
.heart-saved { color: var(--nnz-accent); }
.heart-unsaved {
  color: var(--nnz-muted);
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
