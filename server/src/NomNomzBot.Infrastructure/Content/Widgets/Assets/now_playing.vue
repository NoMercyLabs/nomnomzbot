<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Standing now-playing display driven by the "now_playing" widget event (WidgetNowPlayingHandler:
// { isPlaying, track, artist, artUrl, provider, trackUri }), adapting per the current track's provider:
// Spotify has no visual of its own, so this also becomes the Spotify Connect audio device (merged from
// the old standalone "Spotify Player" widget); YouTube optionally renders as an actual video instead of
// the compact card.
interface NowPlayingConfig {
  layout: string          // 'pill' | 'card'
  showArt: boolean
  showProgressBar: boolean
  provider: string        // '' = show any provider; otherwise only tracks whose payload provider matches
  accentColor: string
  enableAudio: boolean    // become a Spotify Connect device when the current track is Spotify
  youtubeMode: string     // 'card' | 'video'
}

const cfg = reactive<NowPlayingConfig>({
  layout: 'pill',
  showArt: true,
  showProgressBar: true,
  provider: '',
  accentColor: '#9146ff',
  enableAudio: true,
  youtubeMode: 'card',
})

const isPlaying = ref<boolean>(false)
const track = ref<string>('')
const artist = ref<string>('')
const artUrl = ref<string>('')
const trackProvider = ref<string>('')
const trackUri = ref<string>('')
const spotifyStatus = ref<string>('') // '' | 'connecting' | 'active' | 'muted' | 'blocked' | 'error'
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
    marqueeEl.value.classList.remove('animate-marquee')
    marqueeEl.value.style.removeProperty('--marquee-distance')
    const containerWidth = titleEl.value.getBoundingClientRect().width
    const textWidth = marqueeEl.value.getBoundingClientRect().width
    if (textWidth > containerWidth) {
      marqueeEl.value.style.setProperty('--marquee-distance', `${containerWidth - textWidth}px`)
      marqueeEl.value.classList.add('animate-marquee')
    }
  }, 300)
}

const youtubeVideoId = computed<string>(() => {
  if (trackProvider.value !== 'youtube' || !trackUri.value) return ''
  try {
    return new URL(trackUri.value).searchParams.get('v') || ''
  } catch {
    return ''
  }
})
const showYoutubeVideo = computed<boolean>(
  () => cfg.youtubeMode === 'video' && isPlaying.value && !!youtubeVideoId.value
)

function onTrackSavedChanged(d: any): void {
  const data: any = d || {}
  heartIsSaved.value = !!data.isSaved
  heartPulse.value = true
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  heartPulseTimeout = window.setTimeout(() => { heartPulse.value = false }, 1600)
}

function onNowPlaying(d: any): void {
  const data: any = d || {}
  if (cfg.provider && data.provider && data.provider !== cfg.provider) return
  isPlaying.value = !!data.isPlaying
  track.value = data.track || ''
  artist.value = data.artist || ''
  artUrl.value = data.artUrl || ''
  trackProvider.value = data.provider || ''
  trackUri.value = data.trackUri || ''
  durationMs.value = Number.isFinite(Number(data.durationMs)) ? Number(data.durationMs) : 0
  progressMs.value = Number.isFinite(Number(data.progressMs)) ? Number(data.progressMs) : 0
  stopTicking()
  if (isPlaying.value) startTicking()
  refreshMarquee()
  if (trackProvider.value === 'spotify' && cfg.enableAudio) connectSpotify()
}

// Fetch the real current state on mount instead of showing nothing until the next playback change —
// every overlay reload otherwise sat blank until the streamer's next skip/pause/resume.
async function fetchCurrentState(): Promise<void> {
  const token = widgetToken()
  if (!token) return
  try {
    const res = await fetch(`/api/v1/overlay/now-playing?token=${encodeURIComponent(token)}`)
    if (!res.ok) return
    const body = await res.json()
    if (body?.data) onNowPlaying(body.data)
  } catch {
    // Best-effort seed only — the next now_playing hub event still arrives normally.
  }
}

onMounted(() => {
  if (!nnz) return
  fetchCurrentState()
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.layout === 'string' && s.layout) cfg.layout = s.layout
    if (typeof s.showArt === 'boolean') cfg.showArt = s.showArt
    if (typeof s.showProgressBar === 'boolean') cfg.showProgressBar = s.showProgressBar
    if (typeof s.provider === 'string') cfg.provider = s.provider
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.youtubeMode === 'string' && s.youtubeMode) cfg.youtubeMode = s.youtubeMode
    if (typeof s.enableAudio === 'boolean' && s.enableAudio !== cfg.enableAudio) {
      cfg.enableAudio = s.enableAudio
      if (cfg.enableAudio && trackProvider.value === 'spotify') connectSpotify()
      else if (!cfg.enableAudio) disconnectSpotify()
    }
  })
  nnz.on('now_playing', onNowPlaying)
  nnz.on('track_saved_changed', onTrackSavedChanged)
})

onUnmounted(() => {
  stopTicking()
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  if (!nnz) return
  nnz.off('now_playing', onNowPlaying)
  nnz.off('track_saved_changed', onTrackSavedChanged)
  disconnectSpotify()
})

// ── Spotify Connect device (Spotify Web Playback SDK) ─────────────────────────
// Requires Spotify Premium and the streamer having reconnected Spotify with the "streaming" scope
// (surfaced here via spotifyStatus === 'blocked'). Registering the SDK player makes this OBS source a
// selectable device in Spotify Connect — it does NOT transfer playback to itself; the streamer picks the
// active device themselves, same as switching between a phone and a desktop app.

let spotifyPlayer: any = null
let sdkLoadPromise: Promise<void> | null = null

function widgetToken(): string {
  const params = new URLSearchParams(location.search)
  return (window as any).WIDGET_TOKEN || params.get('token') || ''
}

async function fetchPlaybackToken(): Promise<string | null> {
  const token = widgetToken()
  if (!token) return null
  try {
    const res = await fetch(`/api/v1/overlay/spotify-token?token=${encodeURIComponent(token)}`)
    if (!res.ok) {
      spotifyStatus.value = res.status === 403 || res.status === 401 ? 'blocked' : 'error'
      return null
    }
    const body = await res.json()
    return body?.data || null
  } catch {
    spotifyStatus.value = 'error'
    return null
  }
}

function loadSpotifySdk(): Promise<void> {
  if (sdkLoadPromise) return sdkLoadPromise
  sdkLoadPromise = new Promise((resolve, reject) => {
    if ((window as any).Spotify) { resolve(); return }
    ;(window as any).onSpotifyWebPlaybackSDKReady = () => resolve()
    const script = document.createElement('script')
    script.src = 'https://sdk.scdn.co/spotify-player.js'
    script.onerror = () => reject(new Error('Spotify SDK failed to load'))
    document.head.appendChild(script)
  })
  return sdkLoadPromise
}

async function connectSpotify(): Promise<void> {
  if (spotifyPlayer) return // already connected
  spotifyStatus.value = 'connecting'

  const firstToken = await fetchPlaybackToken()
  if (!firstToken) return // fetchPlaybackToken already set spotifyStatus ('blocked' | 'error')

  try {
    await loadSpotifySdk()
  } catch {
    spotifyStatus.value = 'error'
    return
  }

  const Spotify = (window as any).Spotify
  const player = new Spotify.Player({
    name: (window as any).WIDGET_NAME ? `NomNomzBot — ${(window as any).WIDGET_NAME}` : 'NomNomzBot Overlay',
    getOAuthToken: (cb: (token: string) => void) => {
      fetchPlaybackToken().then((t) => { if (t) cb(t) })
    },
    volume: 1.0,
  })

  player.addListener('ready', () => {
    spotifyStatus.value = 'active'
    checkAutoplayAllowed().then((allowed) => { if (!allowed) spotifyStatus.value = 'muted' })
  })
  player.addListener('not_ready', () => { spotifyStatus.value = 'connecting' })
  player.addListener('initialization_error', ({ message }: { message: string }) => {
    console.error('[now_playing] initialization_error:', message)
    spotifyStatus.value = 'error'
  })
  player.addListener('authentication_error', ({ message }: { message: string }) => {
    console.error('[now_playing] authentication_error:', message)
    spotifyStatus.value = 'blocked'
  })
  player.addListener('account_error', ({ message }: { message: string }) => {
    console.error('[now_playing] account_error (non-Premium?):', message)
    spotifyStatus.value = 'blocked'
  }) // non-Premium account
  player.addListener('playback_error', ({ message }: { message: string }) => {
    console.error('[now_playing] playback_error:', message)
  })

  const connected = await player.connect()
  if (!connected) {
    console.error(
      '[now_playing] player.connect() returned false — the SDK refused without firing an error ' +
      'listener. Common cause: this page is not a secure context (EME/DRM audio requires https:// or ' +
      'localhost) — check the widget source URL scheme in OBS.'
    )
    spotifyStatus.value = 'error'
  }
  spotifyPlayer = player
}

function disconnectSpotify(): void {
  if (spotifyPlayer) { spotifyPlayer.disconnect(); spotifyPlayer = null }
  spotifyStatus.value = ''
}

// Chromium suspends a page's audio graph without a user gesture; the SDK still connects and streams
// (network/DRM succeed) but nothing is audible. Probes via a throwaway AudioContext rather than the SDK's
// own (sandboxed in a cross-origin iframe, unreadable). Also covers YouTube's iframe autoplay block.
async function checkAutoplayAllowed(): Promise<boolean> {
  const Ctx = (window as any).AudioContext || (window as any).webkitAudioContext
  if (!Ctx) return true
  const ctx = new Ctx()
  await ctx.resume().catch(() => {})
  const allowed = ctx.state === 'running'
  ctx.close().catch(() => {})
  return allowed
}

function enableAudio(): void {
  checkAutoplayAllowed().then((allowed) => { if (allowed) spotifyStatus.value = 'active' })
}
</script>

<template>
  <iframe
    v-if="showYoutubeVideo"
    class="nnz-youtube-video"
    :src="`https://www.youtube-nocookie.com/embed/${youtubeVideoId}?autoplay=1&controls=0`"
    allow="autoplay; encrypted-media"
    frameborder="0"
  />
  <div
    v-else-if="isPlaying && track"
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
      <div v-if="cfg.showProgressBar" class="bar"><div class="fill" :style="{ width: progressPct + '%' }"></div></div>
    </div>
    <span
      v-if="heartPulse"
      class="heart"
      :class="{ 'heart-saved': heartIsSaved, 'heart-unsaved': !heartIsSaved }"
    >&#10084;</span>
  </div>

  <!-- Spotify Connect device status — quiet while connecting/active (the point is invisible audio, not
       a visual element competing for scene space); only a real problem needs the streamer's attention. -->
  <button
    v-if="cfg.enableAudio && spotifyStatus === 'muted'"
    class="nnz-spotify-status nnz-spotify-enable"
    :style="{ '--accent': cfg.accentColor }"
    @click="enableAudio"
  >
    Click to enable audio playback
  </button>
  <div v-else-if="cfg.enableAudio && spotifyStatus === 'blocked'" class="nnz-spotify-status" :style="{ '--accent': cfg.accentColor }">
    Reconnect Spotify with streaming permission to enable this device (Integrations page).
  </div>
  <div v-else-if="cfg.enableAudio && spotifyStatus === 'error'" class="nnz-spotify-status" :style="{ '--accent': cfg.accentColor }">
    Couldn't start playback — check Spotify Premium and try reloading this source.
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
  animation: nnz-marquee 8s ease-in-out infinite;
}
@keyframes nnz-marquee {
  0%, 15% { transform: translateX(0); }
  50%, 65% { transform: translateX(var(--marquee-distance, 0)); }
  100% { transform: translateX(0); }
}
.artist {
  font-size: 12px;
  opacity: 0.75;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
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
.nnz-youtube-video {
  position: fixed;
  right: 16px;
  bottom: 16px;
  width: 480px;
  height: 270px;
  border: none;
  border-radius: 12px;
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
}
.nnz-spotify-status {
  position: fixed;
  left: 16px;
  bottom: 16px;
  max-width: 46vw;
  padding: 8px 16px;
  border-radius: 8px;
  color: #fff;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  font-size: 12px;
  background: rgba(120, 20, 20, 0.85);
  border: 1px solid #d64545;
}
.nnz-spotify-enable {
  cursor: pointer;
  background: var(--accent);
  border: 1px solid var(--accent);
  font: inherit;
}
</style>
