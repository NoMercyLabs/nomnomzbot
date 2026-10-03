<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface DropMarker { player: string; landed: number; distance: number; hit: boolean }
interface DropResult { player: string; landed: number | null; distance: number | null; won: boolean; payout: number }

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']

const cfg = reactive<NnzWidgetSettings>({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'running' | 'resolved' | 'cancelled'>('lobby')
const target = ref<number>(50)
const radius = ref<number>(10)
const drops = ref<DropMarker[]>([])
const results = ref<DropResult[]>([])
const cancelReason = ref<string>('')
let hideTimer: number | undefined

const zoneStyle = computed(() => ({
  left: Math.max(0, target.value - radius.value) + '%',
  width: Math.min(100, target.value + radius.value) - Math.max(0, target.value - radius.value) + '%',
}))

function reset(): void {
  drops.value = []
  results.value = []
  cancelReason.value = ''
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

// Frames arrive typed by phase; the payload 'kind' separates a round opening from an individual drop.
function onFrame(d: GameFrame): void {
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    target.value = d.target ?? 50
    radius.value = d.radius || 10
    return
  }
  if (d.kind === 'drop') {
    drops.value = [...drops.value, {
      player: d.player,
      landed: d.landed,
      distance: d.distance,
      hit: d.hit,
    }]
    return
  }
  if (d.kind === 'cancelled') {
    phase.value = 'cancelled'
    cancelReason.value = d.reason
    scheduleHide()
    return
  }
  if (d.kind === 'results') {
    phase.value = 'resolved'
    if (d.target != null) target.value = d.target
    results.value = d.results.map((r): DropResult => ({
      player: r.player,
      landed: r.landed ?? null,
      distance: r.distance ?? null,
      won: r.won === true,
      payout: r.payout,
    }))
    scheduleHide()
  }
}

function scheduleHide(): void {
  if (hideTimer) clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => { visible.value = false }, cfg.hideAfterMs)
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (isFinite(Number(s.hideAfterMs)) && Number(s.hideAfterMs) > 0) cfg.hideAfterMs = Number(s.hideAfterMs)
  })
  NomNomz.on('game.lobby', onFrame)
  NomNomz.on('game.running', onFrame)
  NomNomz.on('game.resolved', onFrame)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('game.lobby', onFrame)
  NomNomz.off('game.running', onFrame)
  NomNomz.off('game.resolved', onFrame)
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <div v-if="visible" class="nnz-drop" :style="{ '--accent': cfg.accentColor }">
    <div class="head">
      <span v-if="phase === 'lobby'" class="title">Drop Game — type <b>!drop</b> to play</span>
      <span v-else-if="phase === 'resolved'" class="title">Drop Game — results</span>
      <span v-else-if="phase === 'cancelled'" class="title">Drop Game — cancelled</span>
      <span v-else class="title">Drop Game</span>
    </div>
    <div class="track">
      <div class="zone" :style="zoneStyle"></div>
      <div class="target" :style="{ left: target + '%' }"></div>
      <div
        v-for="d in drops"
        :key="d.player + d.landed"
        class="marker"
        :class="{ hit: d.hit }"
        :style="{ left: d.landed + '%' }"
      >
        <span class="name">{{ d.player }}</span>
      </div>
    </div>
    <div v-if="phase === 'resolved'" class="board">
      <div v-for="r in results" :key="r.player" class="row" :class="{ won: r.won }">
        <span class="player">{{ r.player }}</span>
        <span class="score">{{ r.won ? '+' + r.payout : '—' }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.nnz-drop {
  position: fixed;
  left: 50%;
  bottom: 32px;
  transform: translateX(-50%);
  width: min(720px, 90vw);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.78);
  border-radius: 14px;
  padding: 14px 18px 18px;
}
.head { margin-bottom: 10px; }
.title { font-size: 20px; font-weight: 800; letter-spacing: 0.3px; }
.title b { color: var(--accent, #9146ff); }
.track {
  position: relative;
  height: 46px;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.08);
  overflow: visible;
}
.zone {
  position: absolute;
  top: 0;
  bottom: 0;
  border-radius: 8px;
  background: color-mix(in srgb, var(--accent, #9146ff) 35%, transparent);
}
.target {
  position: absolute;
  top: -4px;
  bottom: -4px;
  width: 3px;
  transform: translateX(-50%);
  background: var(--accent, #9146ff);
  box-shadow: 0 0 12px var(--accent, #9146ff);
}
.marker {
  position: absolute;
  top: 6px;
  width: 10px;
  height: 10px;
  transform: translateX(-50%);
  border-radius: 50%;
  background: #d9d9e3;
}
.marker.hit { background: #3ddc84; box-shadow: 0 0 10px #3ddc84; }
.marker .name {
  position: absolute;
  top: 14px;
  left: 50%;
  transform: translateX(-50%);
  font-size: 11px;
  font-weight: 700;
  white-space: nowrap;
  color: rgba(255, 255, 255, 0.85);
}
.board { margin-top: 12px; display: grid; gap: 4px; }
.row {
  display: flex;
  justify-content: space-between;
  font-size: 15px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 6px;
  color: rgba(255, 255, 255, 0.72);
}
.row.won { color: #3ddc84; background: rgba(61, 220, 132, 0.1); }
</style>
