<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface CrewMember { player: string; stake: number }
interface HeistResult { player: string; stake: number; escaped: boolean; payout: number }

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']

const cfg = reactive({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved' | 'cancelled'>('lobby')
const successChance = ref<number>(0)
const crew = ref<CrewMember[]>([])
const results = ref<HeistResult[]>([])
const cancelReason = ref<string>('')
let hideTimer: number | undefined

function reset(): void {
  successChance.value = 0
  crew.value = []
  results.value = []
  cancelReason.value = ''
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

// The payload's `kind` narrows the typed frame union; a heist sends round_open, join, cancelled and results.
function onFrame(d: GameFrame): void {
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    successChance.value = d.successChance || 0
    return
  }
  if (d.kind === 'join') {
    visible.value = true
    phase.value = 'lobby'
    successChance.value = d.successChance || successChance.value
    crew.value = d.crew ?? crew.value
    return
  }
  if (d.kind === 'cancelled') {
    visible.value = true
    phase.value = 'cancelled'
    cancelReason.value = d.reason
    scheduleHide()
    return
  }
  if (d.kind === 'results') {
    visible.value = true
    phase.value = 'resolved'
    successChance.value = d.successChance || successChance.value
    results.value = d.results.map((r): HeistResult => ({
      player: r.player,
      stake: r.stake ?? 0,
      escaped: r.escaped === true,
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
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <Transition name="nnz">
  <div v-if="visible" class="nnz-widget nnz-card nnz-heist">
    <div class="head">
      <span v-if="phase === 'lobby'" class="nnz-title title">Heist — type <b class="nnz-accent-text">!heist</b> to join the crew</span>
      <span v-else-if="phase === 'cancelled'" class="nnz-title title">Heist — cancelled</span>
      <span v-else class="nnz-title title">Heist — the getaway</span>
      <span v-if="phase !== 'cancelled'" class="nnz-chip odds">{{ Math.round(successChance) }}% escape</span>
    </div>
    <div v-if="phase === 'cancelled'" class="roster">
      <span v-if="cancelReason" class="chip">{{ cancelReason }}</span>
    </div>
    <div v-else class="roster">
      <template v-if="phase === 'lobby'">
        <span v-for="m in crew" :key="m.player" class="chip">{{ m.player }} · {{ m.stake }}</span>
      </template>
      <template v-else>
        <span
          v-for="r in results"
          :key="r.player"
          class="chip"
          :class="{ escaped: r.escaped, caught: !r.escaped }"
        >
          {{ r.player }}<span v-if="r.escaped"> +{{ r.payout }}</span><span v-else> ✗</span>
        </span>
      </template>
    </div>
  </div>
  </Transition>
</template>

<style scoped>
.nnz-heist {
  position: fixed;
  left: 50%;
  bottom: 32px;
  transform: translateX(-50%);
  width: min(720px, 90vw);
}
.head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; gap: 12px; }
.title { font-size: 20px; }
.odds { flex: none; margin-bottom: 0; font-size: 15px; white-space: nowrap; }
.roster { display: flex; flex-wrap: wrap; gap: 6px; }
.chip {
  font-size: 13px;
  font-weight: 700;
  padding: 4px 10px;
  border-radius: var(--nnz-radius-inner);
  background: var(--nnz-track);
  color: var(--nnz-text);
}
.chip.escaped { color: var(--nnz-on-accent); background: var(--nnz-accent); }
.chip.caught { color: var(--nnz-muted); text-decoration: line-through; }
</style>
