<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// The MEDIA SHARE widget: plays the approved viewer-shared items (Twitch clips and YouTube videos) on
// stream. The server drives it with `media_share_command` events (load, play, pause, stop, seek, volume)
// and this page reports its player state back, so an ended or failed item moves the queue on.
// Display-only: no controls. Hidden while nothing plays. Clips play in the NoMercy video player.
interface MediaShareConfig {
  showTitle: boolean
  accentColor: string
  anchor: string
}

const cfg = reactive<MediaShareConfig>({
  showTitle: true,
  accentColor: '#9146ff',
  anchor: 'center',
})

type MediaShareCommand = NnzWidgetEventMap['media_share_command']
type PlayerState = 'idle' | 'playing' | 'paused' | 'ended' | 'error'

// The player module is shared in shape with audio.vue: the IFrame Player API for YouTube.
interface YouTubePlayerHandle {
  loadVideoById(videoId: string): void
  pauseVideo(): void
  playVideo(): void
  stopVideo(): void
  seekTo(seconds: number, allowSeekAhead: boolean): void
  getCurrentTime(): number
  getDuration(): number
  setVolume(volume: number): void
  unMute(): void
  destroy(): void
}
interface YouTubeEvent { data: number, target: YouTubePlayerHandle }
interface YouTubeNamespace {
  Player: new (el: HTMLElement, options: {
    width: string
    height: string
    videoId: string
    playerVars: Record<string, number>
    events: {
      onReady: (e: { target: YouTubePlayerHandle }) => void
      onStateChange: (e: YouTubeEvent) => void
      onError: (e: YouTubeEvent) => void
    }
  }) => YouTubePlayerHandle
}
declare const YT: YouTubeNamespace
interface YouTubeWindow extends Window {
  YT?: YouTubeNamespace
  onYouTubeIframeAPIReady?: () => void
}

// A Twitch clip plays in the published NoMercy video player, vendored at build time and served by the bot
// itself (no third-party CDN). It always picks the highest quality the clip has.
interface ClipItem { id: string, title: string, url: string }
interface ClipPlayer {
  addPlugin(plugin: unknown, options: Record<string, unknown>): ClipPlayer
  setup(config: { playlist: ClipItem[] }): ClipPlayer
  on(event: string, handler: () => void): void
  queue(items: ClipItem[]): void
  playItem(index: number): void
  play(): Promise<void>
  pause(): Promise<void>
  stop(): Promise<void>
  time(): number
  time(seconds: number): Promise<void>
  duration(): number
  volume(): number
  volume(level: number): Promise<void>
}
interface PlayerModule {
  nmplayer(containerId: string): ClipPlayer
  TwitchPlugin: unknown
}
interface PlayerWindow extends Window { NomNomzPlayer?: PlayerModule }

// IFrame API state codes: 0 ended, 1 playing, 2 paused.
const YT_STATE_NAMES: Record<number, PlayerState> = { 0: 'ended', 1: 'playing', 2: 'paused' }
const REPORT_INTERVAL_MS: number = 2000
const PLAYER_SCRIPT_URL: string = '/vendor/nomercy-player.js'
const CLIP_START_TIMEOUT_MS: number = 20000
const CLIP_CONTAINER_ID: string = 'nnz-clip-player'
const CLIP_URL_BASE: string = 'https://clips.twitch.tv/'

const title = ref<string>('')
const sourceType = ref<string>('')
const visible = ref<boolean>(false)
const ytMount = ref<HTMLElement | null>(null)
const card = ref<HTMLElement | null>(null)
const frame = ref<HTMLElement | null>(null)
const cardWidth = ref<string>('')

// The video area stays exactly 16:9. The card takes the widest width whose 16:9 frame, plus the card's own
// padding, border and title, still fits the browser source less the edge offset. It measures on screen
// (getBoundingClientRect includes every zoom), then turns the screen width back into the card's own pixels.
const VIDEO_RATIO: number = 16 / 9
function screenScale(el: HTMLElement): number {
  const own: number = parseFloat(getComputedStyle(el).width)
  return own > 0 ? el.getBoundingClientRect().width / own : 1
}
function fitCard(): void {
  const el: HTMLElement | null = card.value
  const video: HTMLElement | null = frame.value
  const source: HTMLElement | null | undefined = el?.parentElement
  if (!el || !video || !source) return
  const box: DOMRect = el.getBoundingClientRect()
  const inner: DOMRect = video.getBoundingClientRect()
  const room: DOMRect = source.getBoundingClientRect()
  const style: CSSStyleDeclaration = getComputedStyle(el)
  // Each side's margin (else the edge offset), in screen pixels.
  const side = (name: string): number => (parseFloat(style.getPropertyValue(name)) || 0) * screenScale(source)
  // The theme's hard shadow reaches past the card by this much (in the card's own zoomed pixels).
  const reach: number = (parseFloat(style.getPropertyValue('--nnz-shadow-reach')) || 0) * screenScale(el)
  const chromeX: number = box.width - inner.width
  const chromeY: number = box.height - inner.height
  const roomX: number = room.width - side('--nnz-ml') - side('--nnz-mr') - 2 * reach - chromeX
  const roomY: number = room.height - side('--nnz-mt') - side('--nnz-mb') - 2 * reach - chromeY
  const videoWidth: number = Math.max(0, Math.min(roomX, roomY * VIDEO_RATIO))
  cardWidth.value = `${Math.floor((videoWidth + chromeX) / screenScale(el))}px`
}
// The fit waits a frame: a resize inside the observer callback is a "ResizeObserver loop" error in the page.
// A page without ResizeObserver (a test DOM) keeps the first fit only.
const fitObserver: ResizeObserver | null =
  typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => requestAnimationFrame(fitCard))

let requestId: string | null = null
let durationSeconds: number = 0
let volume: number = 100
let state: PlayerState = 'idle'
let ytPlayer: YouTubePlayerHandle | null = null
let ytApiPromise: Promise<void> | null = null
let ytStarting: boolean = false
let ytPendingId: string | null = null
let reportTimer: number | undefined
let clipPlayer: ClipPlayer | null = null
let playerModulePromise: Promise<PlayerModule> | null = null
let clipLoadCount: number = 0
let clipArmed: boolean = false
let clipEverLoaded: boolean = false
let clipFirstPlayPending: boolean = false

function isClip(): boolean {
  return sourceType.value === 'twitch_clip'
}

function positionSeconds(): number {
  if (sourceType.value === 'youtube' && ytPlayer) return ytPlayer.getCurrentTime()
  if (isClip() && clipPlayer) return clipPlayer.time()
  return 0
}

function reportedDuration(): number {
  const playerSeconds: number = isClip() && clipPlayer ? clipPlayer.duration() : 0
  return playerSeconds > 0 ? playerSeconds : durationSeconds
}

// The player takes the command's own 0-100 scale, so the value is passed through unscaled (rounded, clamped).
function reportedVolume(): number {
  return isClip() && clipPlayer ? Math.round(clipPlayer.volume()) : volume
}

function report(next: PlayerState): void {
  state = next
  NomNomz.reportMediaSharePlayerState(requestId, next, Math.round(positionSeconds()), Math.round(reportedDuration()), reportedVolume())
    .catch(() => { /* offline: the next report tells the server again */ })
}

function quiet(action: Promise<void> | undefined): void {
  action?.catch(() => { /* a stopped or unloaded player may refuse; the next command tells it again */ })
}

function startReporting(): void {
  window.clearInterval(reportTimer)
  reportTimer = window.setInterval(() => {
    if (state === 'playing') report('playing')
  }, REPORT_INTERVAL_MS)
}

function loadYouTubeApi(): Promise<void> {
  if (ytApiPromise) return ytApiPromise
  ytApiPromise = new Promise((resolve, reject) => {
    const w: YouTubeWindow = window
    if (w.YT) { resolve(); return }
    w.onYouTubeIframeAPIReady = () => resolve()
    const script: HTMLScriptElement = document.createElement('script')
    script.src = 'https://www.youtube.com/iframe_api'
    script.onerror = () => { ytApiPromise = null; reject(new Error('YouTube IFrame API failed to load')) }
    document.head.appendChild(script)
  })
  return ytApiPromise
}

// A page without a user gesture lets YouTube autoplay only muted; OBS browser sources allow sound, so unmute.
function makeAudible(player: YouTubePlayerHandle): void {
  player.setVolume(volume)
  player.unMute()
}

// The frame and title show only once the item can really play; a failed item never shows them.
function showAndReport(): void {
  visible.value = true
  report('playing')
}

function onYouTubeState(e: YouTubeEvent): void {
  const next: PlayerState | undefined = YT_STATE_NAMES[e.data]
  if (!next) return
  if (next === 'playing') makeAudible(e.target)
  if (next === 'ended') { finish('ended'); return }
  if (next === 'playing') { showAndReport(); return }
  report(next)
}

async function playYouTube(videoId: string): Promise<void> {
  ytPendingId = videoId
  if (ytPlayer) { ytPlayer.loadVideoById(videoId); return }
  if (ytStarting) return
  ytStarting = true
  try {
    await loadYouTubeApi()
  } catch (e) {
    console.error('[media_share]', e)
    ytStarting = false
    report('error')
    return
  }
  if (!ytPendingId || !ytMount.value) { ytStarting = false; return }
  const startedWith: string = ytPendingId
  new YT.Player(ytMount.value, {
    width: '100%',
    height: '100%',
    videoId: startedWith,
    playerVars: { autoplay: 1, controls: 0, rel: 0 },
    events: {
      onReady: (e) => {
        ytPlayer = e.target
        ytStarting = false
        makeAudible(e.target)
        if (ytPendingId && ytPendingId !== startedWith) e.target.loadVideoById(ytPendingId)
      },
      onStateChange: onYouTubeState,
      onError: () => finish('error'),
    },
  })
}

// The player script sets window.NomNomzPlayer; it is added once, from the bot's own origin.
function loadPlayerModule(): Promise<PlayerModule> {
  if (playerModulePromise) return playerModulePromise
  playerModulePromise = new Promise((resolve, reject) => {
    const w: PlayerWindow = window
    if (w.NomNomzPlayer) { resolve(w.NomNomzPlayer); return }
    const script: HTMLScriptElement = document.createElement('script')
    script.src = PLAYER_SCRIPT_URL
    script.onload = () => {
      if (w.NomNomzPlayer) resolve(w.NomNomzPlayer)
      else { playerModulePromise = null; reject(new Error('The clip player script did not define the player')) }
    }
    script.onerror = () => { playerModulePromise = null; reject(new Error('The clip player script failed to load')) }
    document.head.appendChild(script)
  })
  return playerModulePromise
}

// A pause that comes with the clip reaching its end is the end, not a pause.
function isAtEnd(): boolean {
  const length: number = reportedDuration()
  return length > 0 && positionSeconds() >= length - 0.25
}

async function ensureClipPlayer(): Promise<ClipPlayer> {
  if (clipPlayer) return clipPlayer
  const module: PlayerModule = await loadPlayerModule()
  const created: ClipPlayer = module.nmplayer(CLIP_CONTAINER_ID)
    .addPlugin(module.TwitchPlugin, {})
    .setup({ playlist: [] })
  // On a new player queue() already selects the first item, so playItem only loads it: play once it can.
  created.on('canplay', () => {
    if (!clipFirstPlayPending) return
    clipFirstPlayPending = false
    quiet(created.play())
  })
  created.on('playing', () => { if (clipArmed) showAndReport() })
  created.on('pause', () => { if (clipArmed && !isAtEnd()) report('paused') })
  created.on('ended', () => { if (clipArmed) finish('ended') })
  created.on('error', () => { if (clipArmed) finish('error') })
  clipPlayer = created
  return created
}

// Each load queues a fresh item (a new id), so a clip that already played starts again from the top. The
// previous clip is stopped first, and its events are ignored until the new one plays: one ended per clip.
async function playClip(slug: string): Promise<void> {
  clipLoadCount += 1
  const loadNumber: number = clipLoadCount
  clipArmed = false
  try {
    const player: ClipPlayer = await ensureClipPlayer()
    if (loadNumber !== clipLoadCount) return
    await player.stop()
    if (loadNumber !== clipLoadCount) return
    player.queue([{ id: `${requestId ?? slug}:${loadNumber}`, title: title.value, url: CLIP_URL_BASE + encodeURIComponent(slug) }])
    clipArmed = true
    await player.volume(volume)
    clipFirstPlayPending = !clipEverLoaded
    clipEverLoaded = true
    player.playItem(0)
    setTimeout(() => { if (loadNumber === clipLoadCount && !visible.value) finish('error') }, CLIP_START_TIMEOUT_MS)
  } catch (e) {
    console.error('[media_share]', e)
    if (loadNumber === clipLoadCount) finish('error')
  }
}

function finish(next: 'ended' | 'error'): void {
  report(next)
  clearItem()
}

function clearItem(): void {
  visible.value = false
  ytPendingId = null
  ytPlayer?.stopVideo()
  clipArmed = false
  if (clipPlayer) {
    clipLoadCount += 1
    quiet(clipPlayer.stop())
  }
  title.value = ''
}

function onLoad(d: MediaShareCommand): void {
  requestId = d.requestId || null
  sourceType.value = d.sourceType || ''
  title.value = d.title || ''
  durationSeconds = Number(d.durationSeconds) || 0
  if (typeof d.volume === 'number') volume = Math.min(100, Math.max(0, Math.round(d.volume)))
  const mediaRef: string = d.mediaRef || ''
  if (!mediaRef) { report('error'); return }
  visible.value = false
  if (sourceType.value === 'youtube') {
    clipLoadCount += 1
    quiet(clipPlayer?.stop())
    playYouTube(mediaRef)
    return
  }
  if (isClip()) {
    ytPlayer?.stopVideo()
    playClip(mediaRef)
    return
  }
  report('error')
}

function onCommand(d: MediaShareCommand | null | undefined): void {
  if (!d) return
  if (d.command === 'load') { onLoad(d); return }
  if (d.command === 'play') {
    ytPlayer?.playVideo()
    if (isClip()) quiet(clipPlayer?.play())
    return
  }
  if (d.command === 'pause') {
    ytPlayer?.pauseVideo()
    if (isClip()) quiet(clipPlayer?.pause())
    return
  }
  if (d.command === 'seek') {
    if (typeof d.positionSeconds !== 'number') return
    ytPlayer?.seekTo(d.positionSeconds, true)
    if (isClip()) quiet(clipPlayer?.time(d.positionSeconds))
    return
  }
  if (d.command === 'volume') {
    if (typeof d.volume !== 'number') return
    volume = Math.min(100, Math.max(0, Math.round(d.volume)))
    ytPlayer?.setVolume(volume)
    if (isClip()) quiet(clipPlayer?.volume(volume))
    return
  }
  if (d.command === 'stop') {
    report('ended')
    clearItem()
  }
}

onMounted(() => {
  if (card.value?.parentElement) fitObserver?.observe(card.value.parentElement)
  if (card.value) fitObserver?.observe(card.value)
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.showTitle === 'boolean') cfg.showTitle = s.showTitle
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.anchor === 'string' && s.anchor) cfg.anchor = s.anchor
    // The edge offset and scale change the room without resizing anything the observer watches.
    requestAnimationFrame(fitCard)
  })
  NomNomz.on('media_share_command', onCommand)
  startReporting()
})

onUnmounted(() => {
  window.clearInterval(reportTimer)
  fitObserver?.disconnect()
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('media_share_command', onCommand)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only sizes the frame. -->
  <div
    ref="card"
    class="nnz-widget nnz-card nnz-placed nnz-mediashare"
    :class="{ 'is-hidden': !visible }"
    :data-anchor="cfg.anchor"
    :style="cardWidth ? { width: cardWidth } : undefined"
  >
    <div ref="frame" class="frame">
      <div v-show="sourceType === 'twitch_clip'" id="nnz-clip-player" class="player" />
      <div v-show="sourceType === 'youtube'" ref="ytMount" class="player" />
    </div>
    <div v-if="cfg.showTitle && title" class="nnz-row title"><span class="nnz-grow">{{ title }}</span></div>
  </div>
</template>

<style scoped>
/* Starts at the full browser source width less the edge offset; fitCard then narrows it so the 16:9 video
   and the card's padding, border and title fit the height too. The card zoom multiplies the declared size. */
.nnz-mediashare {
  width: calc(100 * var(--nnz-vw, 1vw) / var(--nnz-z) - var(--nnz-inset-left) - var(--nnz-inset-right));
  display: flex;
  flex-direction: column;
  gap: 8px;
}
/* Hidden, not display:none, so a YouTube iframe still loads and plays while the frame is not shown. */
.is-hidden {
  visibility: hidden;
}
.frame {
  flex: none;
  width: 100%;
  aspect-ratio: 16 / 9;
  border-radius: var(--nnz-radius-inner);
  overflow: hidden;
  background: #000; /* theme-exempt: a video's black letterbox */
}
.player {
  width: 100%;
  height: 100%;
  border: 0;
  display: block;
}
.player :deep(video) {
  width: 100%;
  height: 100%;
  object-fit: contain;
}
.title {
  flex: none;
}
</style>
