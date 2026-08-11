<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Standing now-playing display driven by the "now_playing" widget event (WidgetNowPlayingHandler:
// { isPlaying, track }, plus optional artUrl/artist/provider when a richer payload ships). Hidden while
// nothing plays.
interface NowPlayingConfig {
  layout: string          // 'pill' | 'card'
  showArt: boolean        // renders album art only when the payload carries an artUrl
  showProgressBar: boolean // no progress data flows yet — renders an indeterminate sweep while playing
  provider: string        // '' = show any provider; otherwise only tracks whose payload provider matches
  enableAudio: boolean    // opt-in: become the active Spotify Connect device and stream real audio
  accentColor: string
}

const cfg = reactive<NowPlayingConfig>({
  layout: 'pill',
  showArt: true,
  showProgressBar: true,
  provider: '',
  enableAudio: false,
  accentColor: '#9146ff',
})

const isPlaying = ref<boolean>(false)
const track = ref<string>('')
const artist = ref<string>('')
const artUrl = ref<string>('')
const audioStatus = ref<string>('') // '' | 'connecting' | 'active' | 'blocked' | 'error'

function onNowPlaying(d: any): void {
  const data: any = d || {}
  if (cfg.provider && data.provider && data.provider !== cfg.provider) return
  isPlaying.value = !!data.isPlaying
  track.value = data.track || ''
  artist.value = data.artist || ''
  artUrl.value = data.artUrl || ''
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
    // enableAudio only ever turns audio ON here — startEmbeddedPlayback() below is idempotent-guarded
    // (spotifyPlayer !== null), so a settings re-push while already connected is a no-op.
    if (typeof s.enableAudio === 'boolean' && s.enableAudio !== cfg.enableAudio) {
      cfg.enableAudio = s.enableAudio
      if (cfg.enableAudio) startEmbeddedPlayback()
    }
  })
  nnz.on('now_playing', onNowPlaying)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('now_playing', onNowPlaying)
  disconnectEmbeddedPlayback()
})

// ── Embedded playback (Spotify Web Playback SDK) ──────────────────────────────
// Opt-in: the widget becomes a real Spotify Connect device and streams audio itself, rather than just
// showing a passive now-playing display. Requires Spotify Premium and the streamer having reconnected
// Spotify with the "streaming" scope (widget-visible via audioStatus === 'blocked'). The SDK needs a
// live access token via getOAuthToken — fetched from our own overlay endpoint (never the refresh token,
// never exposed anywhere else), scoped to exactly the streaming/playback scopes this channel granted.

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
      audioStatus.value = res.status === 403 || res.status === 401 ? 'blocked' : 'error'
      return null
    }
    const body = await res.json()
    return body?.data || null
  } catch {
    audioStatus.value = 'error'
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

async function startEmbeddedPlayback(): Promise<void> {
  if (spotifyPlayer) return // already connected — settings re-push, don't reconnect
  audioStatus.value = 'connecting'

  const firstToken = await fetchPlaybackToken()
  if (!firstToken) return // fetchPlaybackToken already set audioStatus ('blocked' | 'error')

  try {
    await loadSpotifySdk()
  } catch {
    audioStatus.value = 'error'
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

  player.addListener('ready', ({ device_id }: { device_id: string }) => {
    audioStatus.value = 'active'
    // Become the active device and start playback here — the SDK player already holds a token with
    // user-modify-playback-state, so this talks to Spotify's Web API directly (no backend round-trip).
    fetchPlaybackToken().then((t) => {
      if (!t) return
      fetch('https://api.spotify.com/v1/me/player', {
        method: 'PUT',
        headers: { Authorization: `Bearer ${t}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ device_ids: [device_id], play: true }),
      }).catch(() => {})
    })
  })
  player.addListener('not_ready', () => { audioStatus.value = 'connecting' })
  player.addListener('initialization_error', () => { audioStatus.value = 'error' })
  player.addListener('authentication_error', () => { audioStatus.value = 'blocked' })
  player.addListener('account_error', () => { audioStatus.value = 'blocked' }) // non-Premium account

  const connected = await player.connect()
  if (!connected) audioStatus.value = 'error'
  spotifyPlayer = player
}

function disconnectEmbeddedPlayback(): void {
  if (spotifyPlayer) { spotifyPlayer.disconnect(); spotifyPlayer = null }
  audioStatus.value = ''
}
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
      <div class="track">{{ track }}</div>
      <div v-if="artist" class="artist">{{ artist }}</div>
      <div v-if="cfg.showProgressBar" class="bar"><div class="sweep"></div></div>
    </div>
  </div>
  <!-- Streamer-facing status only — never shown when audio isn't enabled, and never blocks the visual
       now-playing display above (it renders independently, even with nothing currently playing). -->
  <div v-if="cfg.enableAudio && audioStatus === 'blocked'" class="nnz-audio-status">
    Reconnect Spotify with streaming permission to enable audio (Integrations page).
  </div>
  <div v-else-if="cfg.enableAudio && audioStatus === 'error'" class="nnz-audio-status">
    Couldn't start embedded playback — check Spotify Premium and try reloading this source.
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
.nnz-audio-status {
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
</style>
