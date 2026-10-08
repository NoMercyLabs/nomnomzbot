<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// The AUDIO widget: the page where sound, TTS and music play. The SDK does
// the playing (it receives the dispatched utterance and plays the server-synthesised audioUrl through a
// media element, which is the only kind of audio OBS captures from a browser source). This widget exists
// so a channel has ONE well-known page for that, instead of every streamer hand-rolling a custom widget.
//
// It renders nothing by default — add it to OBS, size it 1x1, and leave it. "Control audio via OBS" must
// be ON for the scene to hear it. The optional indicator is for setup: turn it on, speak once, confirm the
// source is alive, turn it back off.
const cfg = reactive<NnzWidgetSettings>({
  showIndicator: false,
  accentColor: '#9146ff',
  enableAudio: true,
})

const speaking = ref<boolean>(false)
const spotifyStatus = ref<string>('')
const trackProvider = ref<string>('')
let musicPoll: number | undefined
let clearTimer: number | undefined

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
let ytStarting = false
let ytRequestedVideoId: string | null = null
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
  ytRequestedVideoId = videoId
  ytLoading = true
  ytPendingSeekSeconds = null
  if (ytPlayer) { ytPlayer.loadVideoById(videoId); return }
  if (ytStarting) return
  ytStarting = true
  try {
    await loadYouTubeApi()
  } catch (e) {
    console.error('[audio]', e)
    ytStarting = false
    return
  }
  ytStarting = false
  if (!ytRequestedVideoId) return
  // Another play event may have created the player while the API loaded; read it fresh (TS narrowed it to null).
  const created: YouTubePlayerHandle | null = currentYouTubePlayer()
  if (created) { created.loadVideoById(ytRequestedVideoId || videoId); return }
  if (!ytMount.value) return
  ytPlayer = new YT.Player(ytMount.value, {
    width: '100%',
    height: '100%',
    videoId: ytRequestedVideoId || videoId,
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
function onYouTubeStop(d?: NnzWidgetEventMap['youtube.stop'] | null): void {
  ytRequestedVideoId = null
  if (!ytPlayer) return
  const videoId: string = ytPlayer.getVideoData().video_id
  const positionMs: number = Math.round(ytPlayer.getCurrentTime() * 1000)
  ytPlayer.stopVideo()
  if ((d as { handoff?: boolean } | null)?.handoff) return
  NomNomz.reportYouTubePlayerState(videoId, 'ENDED', positionMs)
    .catch(() => { /* offline: the bot keeps the video as playing until the next report */ })
}

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
let spotifyAttempt: Promise<void> | null = null
let spotifyGeneration = 0

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

function connectSpotify(): Promise<void> {
  if (spotifyPlayer) return Promise.resolve()
  if (spotifyAttempt) return spotifyAttempt
  spotifyAttempt = openSpotify().finally(() => { spotifyAttempt = null })
  return spotifyAttempt
}

async function openSpotify(): Promise<void> {
  if (spotifyPlayer) return // already connected
  const generation: number = spotifyGeneration
  spotifyStatus.value = 'connecting'

  const firstToken = await fetchPlaybackToken()
  if (generation !== spotifyGeneration) return
  if (!firstToken) return // fetchPlaybackToken already set spotifyStatus ('blocked' | 'error')

  try {
    await loadSpotifySdk()
  } catch {
    spotifyStatus.value = 'error'
    return
  }
  if (generation !== spotifyGeneration) return

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
    console.error('[audio] initialization_error:', message)
    spotifyStatus.value = 'error'
  })
  player.addListener('authentication_error', ({ message }) => {
    console.error('[audio] authentication_error:', message)
    spotifyStatus.value = 'blocked'
  })
  player.addListener('account_error', ({ message }) => {
    console.error('[audio] account_error (non-Premium?):', message)
    spotifyStatus.value = 'blocked'
  }) // non-Premium account
  player.addListener('playback_error', ({ message }) => {
    console.error('[audio] playback_error:', message)
  })

  const connected = await player.connect()
  if (generation !== spotifyGeneration) { player.disconnect(); return }
  if (!connected) {
    console.error(
      '[audio] player.connect() returned false — the SDK refused without firing an error ' +
      'listener. Common cause: this page is not a secure context (EME/DRM audio requires https:// or ' +
      'localhost) — check the widget source URL scheme in OBS.'
    )
    spotifyStatus.value = 'error'
  }
  spotifyPlayer = player
}

function disconnectSpotify(): void {
  spotifyGeneration++
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
function onAudioNowPlaying(d: NnzWidgetEventMap['now_playing'] | null | undefined): void {
  trackProvider.value = d?.provider || ''
  if (trackProvider.value === 'spotify' && cfg.enableAudio) connectSpotify()
}

async function refreshNowPlaying(): Promise<void> {
  try { onAudioNowPlaying(await NomNomz.data.nowPlaying()) } catch { /* next poll or event retries */ }
}

function onSpeak(payload: NnzWidgetEventMap['tts_speak']): void {
  if (!cfg.showIndicator) return
  speaking.value = true
  if (clearTimer) window.clearTimeout(clearTimer)
  // Fall back to a short flash when the utterance carries no duration, so the dot cannot stick on.
  const ms: number = Number(payload?.durationMs) > 0 ? Number(payload.durationMs) : 1500
  clearTimer = window.setTimeout(() => (speaking.value = false), ms)
}

function applySettings(next: Partial<NnzWidgetSettings> | undefined): void {
  if (!next) return
  Object.assign(cfg, next)
  if (cfg.enableAudio && trackProvider.value === 'spotify') connectSpotify()
  else if (!cfg.enableAudio) disconnectSpotify()
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  applySettings(NomNomz.settings)
  NomNomz.onSettings(applySettings)
  NomNomz.on('tts_speak', onSpeak)
  NomNomz.on('now_playing', onAudioNowPlaying)
  refreshNowPlaying()
  musicPoll = window.setInterval(refreshNowPlaying, 5000)
  NomNomz.on('youtube.seek', onYouTubeSeek)
  NomNomz.on('youtube.stop', onYouTubeStop)
  NomNomz.on('youtube.resume', onYouTubeResume)
  NomNomz.on('youtube.pause', onYouTubePause)
  NomNomz.on('youtube.play', onYouTubePlay)
})

onUnmounted(() => {
  if (typeof NomNomz !== 'undefined') NomNomz.off('tts_speak', onSpeak)
  if (typeof NomNomz !== 'undefined') NomNomz.off('youtube.seek', onYouTubeSeek)
  if (typeof NomNomz !== 'undefined') NomNomz.off('youtube.stop', onYouTubeStop)
  if (typeof NomNomz !== 'undefined') NomNomz.off('youtube.resume', onYouTubeResume)
  if (typeof NomNomz !== 'undefined') NomNomz.off('youtube.pause', onYouTubePause)
  if (typeof NomNomz !== 'undefined') NomNomz.off('youtube.play', onYouTubePlay)
  if (typeof NomNomz !== 'undefined') NomNomz.off('now_playing', onAudioNowPlaying)
  if (musicPoll) window.clearInterval(musicPoll)
  disconnectSpotify()
  if (clearTimer) window.clearTimeout(clearTimer)
})
</script>

<template>
  <div class="youtube-audio"><div ref="ytMount" /></div>
  <div v-if="cfg.showIndicator" class="tts-audio" :class="{ speaking }">
    <span class="dot" :style="{ background: cfg.accentColor }" />
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
.youtube-audio { position: fixed; width: 1px; height: 1px; opacity: 0; pointer-events: none; }
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
