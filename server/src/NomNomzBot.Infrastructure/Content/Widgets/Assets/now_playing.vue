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
  })
  nnz.on('now_playing', onNowPlaying)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('now_playing', onNowPlaying)
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
      <div class="track">{{ track }}</div>
      <div v-if="artist" class="artist">{{ artist }}</div>
      <div v-if="cfg.showProgressBar" class="bar"><div class="sweep"></div></div>
    </div>
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
</style>
