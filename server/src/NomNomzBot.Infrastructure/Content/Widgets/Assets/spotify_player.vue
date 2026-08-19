<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { reactive, ref, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// This widget's ENTIRE purpose is to become a real Spotify Connect device and stream audio through this
// OBS browser source — pick it as the active device in Spotify, same as picking a phone or desktop app.
// It renders no now-playing display itself (pair it with the separate "Now Playing" widget for that);
// its only visible output is a small status badge, and only when something needs the streamer's attention.
interface SpotifyPlayerConfig {
  enableAudio: boolean
  accentColor: string
}

const cfg = reactive<SpotifyPlayerConfig>({ enableAudio: true, accentColor: '#9146ff' })
const status = ref<string>('') // '' | 'connecting' | 'active' | 'blocked' | 'error'

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.enableAudio === 'boolean' && s.enableAudio !== cfg.enableAudio) {
      cfg.enableAudio = s.enableAudio
      if (cfg.enableAudio) connect()
      else disconnect()
    }
  })
  if (cfg.enableAudio) connect()
})

onUnmounted(disconnect)

// ── Embedded playback (Spotify Web Playback SDK) ──────────────────────────────
// Requires Spotify Premium and the streamer having reconnected Spotify with the "streaming" scope
// (surfaced here via status === 'blocked'). The SDK needs a live access token via getOAuthToken — fetched
// from our own overlay endpoint (never the refresh token, never exposed anywhere else), scoped to exactly
// the streaming/playback scopes this channel granted.

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
      status.value = res.status === 403 || res.status === 401 ? 'blocked' : 'error'
      return null
    }
    const body = await res.json()
    return body?.data || null
  } catch {
    status.value = 'error'
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

async function connect(): Promise<void> {
  if (spotifyPlayer) return // already connected — settings re-push, don't reconnect
  status.value = 'connecting'

  const firstToken = await fetchPlaybackToken()
  if (!firstToken) return // fetchPlaybackToken already set status ('blocked' | 'error')

  try {
    await loadSpotifySdk()
  } catch {
    status.value = 'error'
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
    status.value = 'active'
    // Become the active device and start playback here — the SDK player already holds a token with
    // user-modify-playback-state, so this talks to Spotify's Web API directly (no backend round-trip).
    fetchPlaybackToken().then((t) => {
      if (!t) return
      fetch('https://api.spotify.com/v1/me/player', {
        method: 'PUT',
        headers: { Authorization: `Bearer ${t}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ device_ids: [device_id], play: true }),
      }).catch((err) => console.error('[spotify_player] activate device failed', err))
    })
  })
  player.addListener('not_ready', () => { status.value = 'connecting' })
  player.addListener('initialization_error', ({ message }: { message: string }) => {
    console.error('[spotify_player] initialization_error:', message)
    status.value = 'error'
  })
  player.addListener('authentication_error', ({ message }: { message: string }) => {
    console.error('[spotify_player] authentication_error:', message)
    status.value = 'blocked'
  })
  player.addListener('account_error', ({ message }: { message: string }) => {
    console.error('[spotify_player] account_error (non-Premium?):', message)
    status.value = 'blocked'
  }) // non-Premium account
  player.addListener('playback_error', ({ message }: { message: string }) => {
    console.error('[spotify_player] playback_error:', message)
  })

  const connected = await player.connect()
  if (!connected) {
    console.error(
      '[spotify_player] player.connect() returned false — the SDK refused without firing an error ' +
      'listener. Common cause: this page is not a secure context (EME/DRM audio requires https:// or ' +
      'localhost) — check the widget source URL scheme in OBS.'
    )
    status.value = 'error'
  }
  spotifyPlayer = player
}

function disconnect(): void {
  if (spotifyPlayer) { spotifyPlayer.disconnect(); spotifyPlayer = null }
  status.value = ''
}
</script>

<template>
  <!-- Deliberately quiet: 'connecting'/'active' render nothing (the point is invisible audio, not a
       visual element competing for scene space) — only a real problem needs the streamer's attention. -->
  <div v-if="cfg.enableAudio && status === 'blocked'" class="nnz-spotify-status" :style="{ '--accent': cfg.accentColor }">
    Reconnect Spotify with streaming permission to enable this device (Integrations page).
  </div>
  <div v-else-if="cfg.enableAudio && status === 'error'" class="nnz-spotify-status" :style="{ '--accent': cfg.accentColor }">
    Couldn't start playback — check Spotify Premium and try reloading this source.
  </div>
</template>

<style scoped>
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
</style>
