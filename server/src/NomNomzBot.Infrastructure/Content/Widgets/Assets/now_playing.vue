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

function onNowPlaying(d: any): void {
  const data: any = d || {}
  if (cfg.provider && data.provider && data.provider !== cfg.provider) return
  isPlaying.value = !!data.isPlaying
  track.value = data.track || ''
  artist.value = data.artist || ''
  artUrl.value = data.artUrl || ''
  trackProvider.value = data.provider || ''
  trackUri.value = data.trackUri || ''
  if (trackProvider.value === 'spotify' && cfg.enableAudio) connectSpotify()
}

onMounted(() => {
  if (!nnz) return
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
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('now_playing', onNowPlaying)
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
      <div class="track">{{ track }}</div>
      <div v-if="artist" class="artist">{{ artist }}</div>
      <div v-if="cfg.showProgressBar" class="bar"><div class="sweep"></div></div>
    </div>
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
  text-overflow: ellipsis;
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
  display: none; /* the pill stays compact; the sweep is a card-layout detail */
}
.sweep {
  width: 40%;
  height: 100%;
  border-radius: 2px;
  background: var(--accent, #9146ff);
  animation: nnz-sweep 2.4s ease-in-out infinite;
}
@keyframes nnz-sweep {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(350%); }
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
