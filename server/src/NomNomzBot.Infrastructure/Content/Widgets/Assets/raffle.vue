<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface Entrant { player: string; stake: number }
interface RaffleResult { player: string; stake: number; won: boolean; payout: number }

const cfg = reactive({
  accentColor: '#9146ff',
  hideAfterMs: 12000,
  anchor: 'bottom',
})

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved'>('lobby')
const pot = ref<number>(0)
const entrants = ref<Entrant[]>([])
const results = ref<RaffleResult[]>([])
const winner = ref<string>('')
const lobbyEndsAt = ref<number>(0)
const now = ref<number>(Date.now())
const drawn = ref<boolean>(false)
let hideTimer: number | undefined
let clockTimer: number | undefined
let drawTimer: number | undefined

// One reel row, in px. The reel spins through the names and stops on the winner.
const ROW = 48
const SPINS = 4
const DRAW_MS = 2600

function reset(): void {
  pot.value = 0
  lobbyEndsAt.value = 0
  drawn.value = false
  if (drawTimer) { clearTimeout(drawTimer); drawTimer = undefined }
  entrants.value = []
  results.value = []
  winner.value = ''
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']

// The payload's `kind` narrows the typed frame union; a raffle sends round_open, join and results.
function onFrame(d: GameFrame): void {
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    lobbyEndsAt.value = Date.now() + (d.lobbySeconds || 0) * 1000
    return
  }
  if (d.kind === 'join') {
    visible.value = true
    phase.value = 'lobby'
    pot.value = d.pot || pot.value
    entrants.value = d.entrants ?? entrants.value
    return
  }
  if (d.kind === 'results') {
    visible.value = true
    phase.value = 'resolved'
    pot.value = d.pot || pot.value
    winner.value = d.winner ?? ''
    results.value = d.results.map((r): RaffleResult => ({
      player: r.player,
      stake: r.stake ?? 0,
      won: r.won === true,
      payout: r.payout,
    }))
    drawn.value = false
    if (drawTimer) clearTimeout(drawTimer)
    drawTimer = window.setTimeout(() => { drawn.value = true }, DRAW_MS)
    scheduleHide()
  }
}

const lobbyLeft = computed<number>(() => Math.max(0, Math.ceil((lobbyEndsAt.value - now.value) / 1000)))
// Each ticket's share of the pot is its chance to win.
function chance(stake: number): number {
  return pot.value > 0 ? Math.round((stake / pot.value) * 100) : 0
}
// The reel: every name a few times over, ending on the winner, so the stop lands on them.
const reel = computed<string[]>(() => {
  const names = results.value.map((r) => r.player)
  if (names.length === 0) return [winner.value]
  const rows: string[] = []
  for (let i = 0; i < SPINS; i++) rows.push(...names)
  rows.push(winner.value)
  return rows
})
const reelStop = computed<string>(() => `translateY(-${(reel.value.length - 1) * ROW}px)`)
const winnerStake = computed<number>(() => results.value.find((r) => r.won)?.stake ?? 0)

function scheduleHide(): void {
  if (hideTimer) clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => { visible.value = false }, cfg.hideAfterMs + DRAW_MS)
}

onMounted(() => {
  clockTimer = window.setInterval(() => { now.value = Date.now() }, 250)
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.anchor === 'string' && s.anchor) cfg.anchor = s.anchor
    if (isFinite(Number(s.hideAfterMs)) && Number(s.hideAfterMs) > 0) cfg.hideAfterMs = Number(s.hideAfterMs)
  })
  NomNomz.on('game.lobby', onFrame)
  NomNomz.on('game.running', onFrame)
  NomNomz.on('game.resolved', onFrame)
})

onUnmounted(() => {
  if (clockTimer) clearInterval(clockTimer)
  if (drawTimer) clearTimeout(drawTimer)
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
    <div v-if="visible" class="nnz-widget nnz-card nnz-placed nnz-raffle" :data-anchor="cfg.anchor">
      <div class="head">
        <span class="nnz-chip title">Raffle</span>
        <span v-if="phase === 'lobby'" class="nnz-sub hint">Type <b class="nnz-accent-text">!raffle &lt;amount&gt;</b> to buy tickets</span>
        <span v-else-if="!drawn" class="nnz-sub hint">Drawing a winner</span>
        <span v-else class="nnz-sub hint">The winner takes the whole pot</span>
        <span v-if="phase === 'lobby' && lobbyLeft > 0" class="nnz-title count">{{ lobbyLeft }}<small>s</small></span>
      </div>

      <div class="stage">
        <div class="potbox">
          <span class="nnz-sub label">Pot</span>
          <span class="nnz-title potnum">{{ pot }}</span>
        </div>

        <div v-if="phase === 'lobby'" class="tickets">
          <span v-for="e in entrants" :key="e.player" class="chip in">
            {{ e.player }} <b>{{ chance(e.stake) }}%</b>
          </span>
          <span v-if="entrants.length === 0" class="nnz-sub">No tickets yet. More points, better odds.</span>
        </div>

        <div v-else class="draw">
          <div class="window" :class="{ landed: drawn }">
            <div class="strip" :style="{ '--stop': reelStop, animationDuration: DRAW_MS + 'ms' }">
              <div v-for="(name, i) in reel" :key="i" class="name">{{ name }}</div>
            </div>
          </div>
          <div class="nnz-sub foot" :class="{ shown: drawn }">
            Won <b class="nnz-accent-text">{{ pot }}</b> with {{ chance(winnerStake) }}% of the tickets
          </div>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.nnz-raffle {
  width: 720px;
}
.head { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
.title { margin-bottom: 0; }
.hint { margin: 0; flex: 1; }
.count { flex: none; font-size: 34px; font-weight: 900; line-height: 1; margin: 0; font-variant-numeric: tabular-nums; }
.count small { font-size: 16px; margin-left: 1px; }
.stage { display: flex; gap: 18px; align-items: center; }
.potbox {
  flex: none;
  min-width: 140px;
  padding: 12px 16px;
  border-radius: var(--nnz-radius-inner);
  background: var(--nnz-track);
  text-align: center;
}
.label { margin: 0; display: block; }
.potnum { font-size: 40px; font-weight: 900; line-height: 1.05; margin: 0; font-variant-numeric: tabular-nums; }
.tickets { flex: 1; display: flex; flex-wrap: wrap; gap: 6px; }
.tickets b { color: var(--nnz-muted); font-variant-numeric: tabular-nums; }
.draw { flex: 1; min-width: 0; }
.window {
  height: 48px;
  overflow: hidden;
  border-radius: var(--nnz-radius-inner);
  border: 2px solid var(--nnz-track);
  transition: border-color 300ms;
}
.window.landed { border-color: var(--nnz-accent); }
.strip { animation-name: spin-reel; animation-timing-function: cubic-bezier(.12,.8,.2,1); animation-fill-mode: forwards; }
@keyframes spin-reel { from { transform: translateY(0); } to { transform: var(--stop); } }
.name {
  height: 48px;
  line-height: 48px;
  padding: 0 14px;
  font-size: 26px;
  font-weight: 900;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--nnz-text);
}
.foot { margin: 8px 0 0; opacity: 0; transition: opacity 400ms; }
.foot.shown { opacity: 1; }
.chip {
  font-size: 13px;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: var(--nnz-radius-inner);
  background: var(--nnz-track);
  color: var(--nnz-text);
}
.chip.in { animation: pop 260ms ease-out; }
@keyframes pop { from { transform: scale(0.6); opacity: 0; } }
@media (prefers-reduced-motion: reduce) { .strip { animation-duration: 1ms !important; } .chip.in { animation: none; } }
</style>
