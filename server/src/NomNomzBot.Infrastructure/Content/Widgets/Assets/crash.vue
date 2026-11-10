<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
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
  // A reloaded overlay is replayed the opening frame and the newest one, so every frame shows the widget and
  // carries what it needs to rebuild the view on its own.
  if (d.kind === 'join') {
    visible.value = true
    crewCount.value = d.crewSize ?? crewCount.value + 1
    if (d.phase === 'running') {
      phase.value = 'running'
      multiplier.value = d.entry || multiplier.value
    }
    return
  }
  if (d.kind === 'progress') {
    visible.value = true
    phase.value = 'running'
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'cashout') {
    visible.value = true
    phase.value = 'running'
    crewCount.value = d.crewSize ?? crewCount.value
    multiplier.value = d.multiplier || multiplier.value
    cashed.value = [...cashed.value, {
      player: d.player,
      multiplier: d.multiplier,
      payout: d.payout,
    }]
    return
  }
  if (d.kind === 'bust') {
    visible.value = true
    phase.value = 'crashed'
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'cap') {
    visible.value = true
    capped.value = true
    multiplier.value = d.multiplier || multiplier.value
    return
  }
  if (d.kind === 'results') {
    visible.value = true
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
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <Transition name="nnz">
  <div v-if="visible" class="nnz-widget nnz-card nnz-crash">
    <div class="head">
      <span v-if="phase === 'lobby'" class="nnz-chip title">Crash — type !crash to buy in</span>
      <span v-else-if="phase === 'running'" class="nnz-chip title">Crash — !crash to cash out</span>
      <span v-else class="nnz-chip title">Crash</span>
    </div>
    <div
      class="nnz-title big"
      :class="{ crashed: phase === 'crashed' || (phase === 'resolved' && !capped), win: capped }"
    >
      {{ bigLabel }}
      <span v-if="phase === 'crashed' || (phase === 'resolved' && !capped)" class="tag">BUST</span>
      <span v-else-if="capped" class="tag">MAX</span>
    </div>
    <div v-if="phase === 'lobby'" class="nnz-sub sub">{{ crewCount }} in</div>
    <div v-else-if="phase !== 'resolved'" class="cashrow">
      <span v-for="c in cashed" :key="c.player + c.multiplier" class="chip out">
        {{ c.player }} @ {{ c.multiplier.toFixed(2) }}×
      </span>
    </div>
    <div v-else class="board">
      <div v-for="r in results" :key="r.player" class="nnz-row" :class="{ 'is-first won': r.payout > 0 }">
        <span class="nnz-grow player">{{ r.player }}</span>
        <span class="score">{{ r.payout > 0 ? '+' + r.payout : '—' }}</span>
      </div>
    </div>
  </div>
  </Transition>
</template>

<style scoped>
.nnz-crash {
  position: fixed;
  left: 50%;
  bottom: 32px;
  transform: translateX(-50%);
  width: min(560px, 90vw);
  text-align: center;
}
.head { margin-bottom: 4px; }
.big {
  font-size: 64px;
  font-weight: 900;
  line-height: 1.05;
  color: var(--nnz-accent);
}
.big.crashed { color: var(--nnz-muted); text-shadow: none; }
.big.win { color: var(--nnz-accent); }
.tag { display: block; font-size: 16px; font-weight: 800; letter-spacing: 2px; color: var(--nnz-text); }
.sub { margin-top: 6px; }
.cashrow { margin-top: 10px; display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; }
.chip {
  font-size: 13px;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: var(--nnz-radius-inner);
}
.chip.out { color: var(--nnz-on-accent); background: var(--nnz-accent); }
.board { margin-top: 12px; text-align: left; }
.nnz-row { color: var(--nnz-muted); }
.nnz-row.won { color: var(--nnz-text); }
.score { flex: none; font-variant-numeric: tabular-nums; }
</style>
