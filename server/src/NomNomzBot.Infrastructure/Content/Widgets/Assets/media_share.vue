<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// The MEDIA SHARE widget: plays the approved viewer-shared items (Twitch clips and YouTube videos) on
// stream. The server drives it with `media_share_command` events (load, play, pause, stop, seek, volume)
// and this page reports its player state back, so an ended or failed item moves the queue on.
// Display-only: no controls. Hidden while nothing plays.
interface MediaShareConfig {
  showTitle: boolean
  accentColor: string
}

const cfg = reactive<MediaShareConfig>({
  showTitle: true,
  accentColor: '#9146ff',
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

// IFrame API state codes: 0 ended, 1 playing, 2 paused.
const YT_STATE_NAMES: Record<number, PlayerState> = { 0: 'ended', 1: 'playing', 2: 'paused' }
const REPORT_INTERVAL_MS: number = 2000
const CLIP_END_GRACE_MS: number = 1000

const title = ref<string>('')
const sourceType = ref<string>('')
const clipUrl = ref<string>('')
const visible = ref<boolean>(false)
const ytMount = ref<HTMLElement | null>(null)

let requestId: string | null = null
let durationSeconds: number = 0
let volume: number = 100
let state: PlayerState = 'idle'
let ytPlayer: YouTubePlayerHandle | null = null
let ytApiPromise: Promise<void> | null = null
let ytStarting: boolean = false
let ytPendingId: string | null = null
let reportTimer: number | undefined
let clipEndTimer: number | undefined
let clipStartedAt: number = 0

function positionSeconds(): number {
  if (sourceType.value === 'youtube' && ytPlayer) return ytPlayer.getCurrentTime()
  if (sourceType.value === 'twitch_clip' && clipStartedAt) return (Date.now() - clipStartedAt) / 1000
  return 0
}

function report(next: PlayerState): void {
  state = next
  NomNomz.reportMediaSharePlayerState(requestId, next, Math.round(positionSeconds()), Math.round(durationSeconds), volume)
    .catch(() => { /* offline: the next report tells the server again */ })
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

function onYouTubeState(e: YouTubeEvent): void {
  const next: PlayerState | undefined = YT_STATE_NAMES[e.data]
  if (!next) return
  if (next === 'playing') e.target.setVolume(volume)
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
        e.target.setVolume(volume)
        if (ytPendingId && ytPendingId !== startedWith) e.target.loadVideoById(ytPendingId)
      },
      onStateChange: onYouTubeState,
      onError: () => report('error'),
    },
  })
}

// A clip iframe has no control API: playing is reported on load, ended by a timer of the clip length.
function playClip(clip: string): void {
  clipUrl.value = 'https://clips.twitch.tv/embed?clip=' + encodeURIComponent(clip)
    + '&parent=' + encodeURIComponent(window.location.hostname) + '&autoplay=true'
  clipStartedAt = Date.now()
  window.clearTimeout(clipEndTimer)
  if (durationSeconds > 0)
    clipEndTimer = window.setTimeout(() => finish('ended'), durationSeconds * 1000 + CLIP_END_GRACE_MS)
  report('playing')
}

function finish(next: 'ended' | 'error'): void {
  window.clearTimeout(clipEndTimer)
  report(next)
  clearItem()
}

function clearItem(): void {
  visible.value = false
  clipUrl.value = ''
  clipStartedAt = 0
  ytPendingId = null
  ytPlayer?.stopVideo()
  title.value = ''
}

function onLoad(d: MediaShareCommand): void {
  requestId = d.requestId || null
  sourceType.value = d.sourceType || ''
  title.value = d.title || ''
  durationSeconds = Number(d.durationSeconds) || 0
  if (typeof d.volume === 'number') volume = d.volume
  const mediaRef: string = d.mediaRef || ''
  if (!mediaRef) { report('error'); return }
  visible.value = true
  if (sourceType.value === 'youtube') {
    clipUrl.value = ''
    playYouTube(mediaRef)
    return
  }
  if (sourceType.value === 'twitch_clip') {
    playClip(mediaRef)
    return
  }
  report('error')
}

function onCommand(d: MediaShareCommand | null | undefined): void {
  if (!d) return
  if (d.command === 'load') { onLoad(d); return }
  if (d.command === 'play') { ytPlayer?.playVideo(); if (!ytPlayer && clipUrl.value) report('playing'); return }
  if (d.command === 'pause') { ytPlayer?.pauseVideo(); return }
  if (d.command === 'seek') {
    if (typeof d.positionSeconds === 'number') ytPlayer?.seekTo(d.positionSeconds, true)
    return
  }
  if (d.command === 'volume') {
    if (typeof d.volume !== 'number') return
    volume = Math.min(100, Math.max(0, Math.round(d.volume)))
    ytPlayer?.setVolume(volume)
    return
  }
  if (d.command === 'stop') {
    window.clearTimeout(clipEndTimer)
    report('ended')
    clearItem()
  }
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.showTitle === 'boolean') cfg.showTitle = s.showTitle
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('media_share_command', onCommand)
  startReporting()
})

onUnmounted(() => {
  window.clearInterval(reportTimer)
  window.clearTimeout(clipEndTimer)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('media_share_command', onCommand)
})
</script>

<template>
  <div v-show="visible" class="nnz-mediashare" :style="{ '--accent': cfg.accentColor }">
    <div class="frame">
      <iframe
        v-if="sourceType === 'twitch_clip' && clipUrl"
        class="player"
        :src="clipUrl"
        allow="autoplay; fullscreen"
        @error="finish('error')"
      />
      <div v-show="sourceType === 'youtube'" ref="ytMount" class="player" />
    </div>
    <div v-if="cfg.showTitle && title" class="title">{{ title }}</div>
  </div>
</template>

<style scoped>
/* Display-only overlay: no action buttons, so no primary action. Outer radius 12px with 8px padding
   makes the inner frame 4px; the accent marks only the title's left rule. */
.nnz-mediashare {
  box-sizing: border-box;
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 8px;
  border-radius: 12px;
  background: rgba(0, 0, 0, 0.72);
  color: #fff;
  font-family: system-ui, sans-serif;
}
.frame {
  flex: 1;
  min-height: 0;
  border-radius: 4px;
  overflow: hidden;
  background: #000;
}
.player {
  width: 100%;
  height: 100%;
  border: 0;
  display: block;
}
.title {
  flex: none;
  padding-left: 8px;
  border-left: 3px solid var(--accent, #9146ff);
  font-size: 14px;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
