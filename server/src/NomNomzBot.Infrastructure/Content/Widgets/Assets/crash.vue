<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface CashOut { player: string; multiplier: number; payout: number }
interface CrashResult { player: string; stake: number; cashedAt: number | null; payout: number }

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']

const cfg = reactive<NnzWidgetSettings>({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'running' | 'crashed' | 'resolved'>('lobby')
const multiplier = ref<number>(1)
const crewCount = ref<number>(0)
const cashed = ref<CashOut[]>([])
const results = ref<CrashResult[]>([])
const capped = ref<boolean>(false)
let hideTimer: number | undefined

function reset(): void {
  multiplier.value = 1
  crewCount.value = 0
  cashed.value = []
  results.value = []
  capped.value = false
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

function onFrame(d: GameFrame): void {
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    return
  }
  if (d.kind === 'join') {
    crewCount.value += 1
    return
  }
  if (d.kind === 'progress') {
    phase.value = 'running'
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'cashout') {
    cashed.value = [...cashed.value, {
      player: d.player,
      multiplier: d.multiplier,
      payout: d.payout,
    }]
    return
  }
  if (d.kind === 'bust') {
    phase.value = 'crashed'
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'cap') {
    capped.value = true
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'results') {
    phase.value = 'resolved'
    capped.value = d.capped === true
    multiplier.value = d.crashedAt || multiplier.value
    results.value = d.results.map((r): CrashResult => ({
      player: r.player,
      stake: r.stake ?? 0,
      cashedAt: r.cashedAt ?? null,
      payout: r.payout,
    }))
    scheduleHide()
  }
}

const bigLabel = computed<string>(() => multiplier.value.toFixed(2) + '×')

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
  <div v-if="visible" class="nnz-crash" :style="{ '--accent': cfg.accentColor }">
    <div class="head">
      <span v-if="phase === 'lobby'" class="title">Crash — type <b>!crash</b> to buy in</span>
      <span v-else-if="phase === 'running'" class="title">Crash — <b>!crash</b> to cash out</span>
      <span v-else class="title">Crash</span>
    </div>
    <div
      class="big"
      :class="{ crashed: phase === 'crashed' || (phase === 'resolved' && !capped), win: capped }"
    >
      {{ bigLabel }}
      <span v-if="phase === 'crashed' || (phase === 'resolved' && !capped)" class="tag">BUST</span>
      <span v-else-if="capped" class="tag">MAX</span>
    </div>
    <div v-if="phase === 'lobby'" class="sub">{{ crewCount }} in</div>
    <div v-else-if="phase !== 'resolved'" class="cashrow">
      <span v-for="c in cashed" :key="c.player + c.multiplier" class="chip out">
        {{ c.player }} @ {{ c.multiplier.toFixed(2) }}×
      </span>
    </div>
    <div v-else class="board">
      <div v-for="r in results" :key="r.player" class="row" :class="{ won: r.payout > 0 }">
        <span class="player">{{ r.player }}</span>
        <span class="score">{{ r.payout > 0 ? '+' + r.payout : '—' }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.nnz-crash {
  position: fixed;
  left: 50%;
  bottom: 32px;
  transform: translateX(-50%);
  width: min(560px, 90vw);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.78);
  border-radius: 14px;
  padding: 14px 18px 18px;
  text-align: center;
}
.head { margin-bottom: 8px; }
.title { font-size: 18px; font-weight: 800; letter-spacing: 0.3px; }
.title b { color: var(--accent, #9146ff); }
.big {
  font-size: 64px;
  font-weight: 900;
  line-height: 1.05;
  color: var(--accent, #9146ff);
  text-shadow: 0 2px 24px color-mix(in srgb, var(--accent, #9146ff) 55%, transparent);
}
.big.crashed { color: #ff6b6b; text-shadow: 0 2px 24px rgba(255, 107, 107, 0.5); }
.big.win { color: #3ddc84; text-shadow: 0 2px 24px rgba(61, 220, 132, 0.5); }
.tag { display: block; font-size: 16px; font-weight: 800; letter-spacing: 2px; }
.sub { margin-top: 6px; font-size: 15px; font-weight: 700; color: rgba(255, 255, 255, 0.7); }
.cashrow { margin-top: 10px; display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; }
.chip {
  font-size: 13px;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
}
.chip.out { color: #3ddc84; background: rgba(61, 220, 132, 0.14); }
.board { margin-top: 12px; display: grid; gap: 4px; text-align: left; }
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
