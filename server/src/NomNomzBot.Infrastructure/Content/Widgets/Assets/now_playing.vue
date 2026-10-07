<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

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
// Who redeemed this track via !sr — empty string (never rendered) when the streamer started it themselves.
const requestedBy = ref<string>('')
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

// ── YouTube player (IFrame Player API) ────────────────────────────────────────
// The bot hands the head of the song queue over as `youtube.play`; this page plays it and reports PLAYING,
// PAUSED and ENDED with the position (ms) back to the bot. The player stays mounted while hidden so audio plays
// in card mode too.
interface YouTubePlayerHandle {
  loadVideoById(videoId: string): void
  pauseVideo(): void
  playVideo(): void
  stopVideo(): void
  seekTo(seconds: number, allowSeekAhead: boolean): void
  getCurrentTime(): number
  getVideoData(): { video_id: string }
}
interface YouTubeStateEvent { data: number, target: YouTubePlayerHandle }
interface YouTubeNamespace {
  Player: new (el: HTMLElement, options: {
    width: string
    height: string
    videoId: string
    playerVars: Record<string, number>
    events: { onStateChange: (e: YouTubeStateEvent) => void }
  }) => YouTubePlayerHandle
}
declare const YT: YouTubeNamespace
interface YouTubeWindow extends Window {
  YT?: YouTubeNamespace
  onYouTubeIframeAPIReady?: () => void
}

const ytMount = ref<HTMLElement | null>(null)
let ytPlayer: YouTubePlayerHandle | null = null
let ytApiPromise: Promise<void> | null = null
// A video was handed over and has not reported PLAYING yet; a seek that arrives meanwhile waits for it.
let ytLoading: boolean = false
let ytPendingSeekSeconds: number | null = null

// IFrame API state codes: 0 ended, 1 playing, 2 paused.
const YT_STATE_NAMES: Record<number, string> = { 0: 'ENDED', 1: 'PLAYING', 2: 'PAUSED' }

function currentYouTubePlayer(): YouTubePlayerHandle | null {
  return ytPlayer
}

function loadYouTubeApi(): Promise<void> {
  if (ytApiPromise) return ytApiPromise
  ytApiPromise = new Promise((resolve, reject) => {
    const w: YouTubeWindow = window
    if (w.YT) { resolve(); return }
    w.onYouTubeIframeAPIReady = () => resolve()
    const script = document.createElement('script')
    script.src = 'https://www.youtube.com/iframe_api'
    script.onerror = () => { ytApiPromise = null; reject(new Error('YouTube IFrame API failed to load')) }
    document.head.appendChild(script)
  })
  return ytApiPromise
}

function applyPendingYouTubeSeek(player: YouTubePlayerHandle): void {
  ytLoading = false
  if (ytPendingSeekSeconds === null) return
  player.seekTo(ytPendingSeekSeconds, true)
  ytPendingSeekSeconds = null
}

function reportYouTubeState(player: YouTubePlayerHandle, code: number): void {
  const state: string | undefined = YT_STATE_NAMES[code]
  if (!state) return
  if (state === 'PLAYING') applyPendingYouTubeSeek(player)
  NomNomz.reportYouTubePlayerState(player.getVideoData().video_id, state, Math.round(player.getCurrentTime() * 1000))
    .catch(() => { /* offline: the next state change reports again */ })
}

async function onYouTubePlay(d: NnzWidgetEventMap['youtube.play'] | null | undefined): Promise<void> {
  const videoId: string = d?.videoId || ''
  if (!videoId) return
  ytLoading = true
  ytPendingSeekSeconds = null
  if (ytPlayer) { ytPlayer.loadVideoById(videoId); return }
  try {
    await loadYouTubeApi()
  } catch (e) {
    console.error('[now_playing]', e)
    return
  }
  // Another play event may have created the player while the API loaded; read it fresh (TS narrowed it to null).
  const created: YouTubePlayerHandle | null = currentYouTubePlayer()
  if (created) { created.loadVideoById(videoId); return }
  if (!ytMount.value) return
  ytPlayer = new YT.Player(ytMount.value, {
    width: '100%',
    height: '100%',
    videoId,
    playerVars: { autoplay: 1, controls: 0 },
    events: {
      onStateChange: (e) => reportYouTubeState(e.target, e.data),
    },
  })
}

// The player's own state changes then report PAUSED / PLAYING back to the bot.
function onYouTubePause(): void {
  ytPlayer?.pauseVideo()
}

function onYouTubeResume(): void {
  ytPlayer?.playVideo()
}

// Right after `youtube.play` the new video is still loading, so the position is held until it reports PLAYING.
function onYouTubeSeek(d: NnzWidgetEventMap['youtube.seek'] | null | undefined): void {
  if (typeof d?.positionMs !== 'number') return
  const seconds: number = d.positionMs / 1000
  if (ytLoading || !ytPlayer) { ytPendingSeekSeconds = seconds; return }
  ytPlayer.seekTo(seconds, true)
}

// stopVideo() emits state 5 (cued), which YT_STATE_NAMES ignores, so the end is reported explicitly.
function onYouTubeStop(): void {
  if (!ytPlayer) return
  const videoId: string = ytPlayer.getVideoData().video_id
  const positionMs: number = Math.round(ytPlayer.getCurrentTime() * 1000)
  ytPlayer.stopVideo()
  NomNomz.reportYouTubePlayerState(videoId, 'ENDED', positionMs)
    .catch(() => { /* offline: the bot keeps the video as playing until the next report */ })
}

function onTrackSavedChanged(d: NnzWidgetEventMap['track_saved_changed'] | null | undefined): void {
  const data: Partial<NnzWidgetEventMap['track_saved_changed']> = d || {}
  heartIsSaved.value = !!data.isSaved
  heartPulse.value = true
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  heartPulseTimeout = window.setTimeout(() => { heartPulse.value = false }, 1600)
}

function onNowPlaying(d: NnzWidgetEventMap['now_playing'] | null | undefined): void {
  const data: Partial<NnzWidgetEventMap['now_playing']> = d || {}
  if (cfg.provider && data.provider && data.provider !== cfg.provider) return
  isPlaying.value = !!data.isPlaying
  track.value = data.track || ''
  artist.value = data.artist || ''
  artUrl.value = data.artUrl || ''
  trackProvider.value = data.provider || ''
  trackUri.value = data.trackUri || ''
  requestedBy.value = typeof data.requestedBy === 'string' ? data.requestedBy : ''
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
  try {
    const current: NnzWidgetEventMap['now_playing'] | null = await NomNomz.data.nowPlaying()
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
    if (typeof s.youtubeMode === 'string' && s.youtubeMode) cfg.youtubeMode = s.youtubeMode
    if (typeof s.enableAudio === 'boolean' && s.enableAudio !== cfg.enableAudio) {
      cfg.enableAudio = s.enableAudio
      if (cfg.enableAudio && trackProvider.value === 'spotify') connectSpotify()
      else if (!cfg.enableAudio) disconnectSpotify()
    }
  })
  NomNomz.on('now_playing', onNowPlaying)
  NomNomz.on('track_saved_changed', onTrackSavedChanged)
  NomNomz.on('youtube.play', onYouTubePlay)
  NomNomz.on('youtube.pause', onYouTubePause)
  NomNomz.on('youtube.resume', onYouTubeResume)
  NomNomz.on('youtube.stop', onYouTubeStop)
  NomNomz.on('youtube.seek', onYouTubeSeek)
})

onUnmounted(() => {
  stopTicking()
  if (heartPulseTimeout) window.clearTimeout(heartPulseTimeout)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('now_playing', onNowPlaying)
  NomNomz.off('track_saved_changed', onTrackSavedChanged)
  NomNomz.off('youtube.play', onYouTubePlay)
  NomNomz.off('youtube.pause', onYouTubePause)
  NomNomz.off('youtube.resume', onYouTubeResume)
  NomNomz.off('youtube.stop', onYouTubeStop)
  NomNomz.off('youtube.seek', onYouTubeSeek)
  disconnectSpotify()
})

// ── Spotify Connect device (Spotify Web Playback SDK) ─────────────────────────
// Requires Spotify Premium and the streamer having reconnected Spotify with the "streaming" scope
// (surfaced here via spotifyStatus === 'blocked'). Registering the SDK player makes this OBS source a
// selectable device in Spotify Connect — it does NOT transfer playback to itself; the streamer picks the
// active device themselves, same as switching between a phone and a desktop app.

// The slice of the Spotify Web Playback SDK this widget uses (the SDK ships no TypeScript declarations here).
interface SpotifyPlayer {
  addListener(event: string, listener: (arg: { message: string }) => void): boolean
  connect(): Promise<boolean>
  disconnect(): void
}
interface SpotifyPlayerOptions {
  name: string
  getOAuthToken: (cb: (token: string) => void) => void
  volume: number
}
interface SpotifyNamespace { Player: new (options: SpotifyPlayerOptions) => SpotifyPlayer }
interface OverlayWindow extends Window {
  Spotify?: SpotifyNamespace
  onSpotifyWebPlaybackSDKReady?: () => void
  AudioContext?: typeof AudioContext
  webkitAudioContext?: typeof AudioContext
}
const overlayWindow: OverlayWindow = window

let spotifyPlayer: SpotifyPlayer | null = null
let sdkLoadPromise: Promise<void> | null = null

async function fetchPlaybackToken(): Promise<string | null> {
  const answer = await NomNomz.spotify.playbackToken()
  if ('token' in answer) return answer.token
  spotifyStatus.value = answer.error
  return null
}

function loadSpotifySdk(): Promise<void> {
  if (sdkLoadPromise) return sdkLoadPromise
  sdkLoadPromise = new Promise((resolve, reject) => {
    if (overlayWindow.Spotify) { resolve(); return }
    overlayWindow.onSpotifyWebPlaybackSDKReady = () => resolve()
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

  const Spotify: SpotifyNamespace | undefined = overlayWindow.Spotify
  if (!Spotify) { spotifyStatus.value = 'error'; return }
  const widgetName: string = NomNomz.widget.name
  const player: SpotifyPlayer = new Spotify.Player({
    name: widgetName ? `NomNomzBot — ${widgetName}` : 'NomNomzBot Overlay',
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
  player.addListener('initialization_error', ({ message }) => {
    console.error('[now_playing] initialization_error:', message)
    spotifyStatus.value = 'error'
  })
  player.addListener('authentication_error', ({ message }) => {
    console.error('[now_playing] authentication_error:', message)
    spotifyStatus.value = 'blocked'
  })
  player.addListener('account_error', ({ message }) => {
    console.error('[now_playing] account_error (non-Premium?):', message)
    spotifyStatus.value = 'blocked'
  }) // non-Premium account
  player.addListener('playback_error', ({ message }) => {
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
  const Ctx: typeof AudioContext | undefined = overlayWindow.AudioContext || overlayWindow.webkitAudioContext
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
  <div :class="showYoutubeVideo ? 'nnz-youtube-video' : 'nnz-youtube-hidden'">
    <div ref="ytMount" />
  </div>
  <div
    v-if="!showYoutubeVideo && isPlaying && track"
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
      <div v-if="requestedBy" class="requester">requested by {{ requestedBy }}</div>
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
.requester {
  font-size: 11px;
  opacity: 0.6;
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
.nnz-youtube-hidden {
  position: fixed;
  width: 1px;
  height: 1px;
  opacity: 0;
  pointer-events: none;
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
