<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
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
  <div v-if="visible" class="nnz-heist" :style="{ '--accent': cfg.accentColor }">
    <div class="head">
      <span v-if="phase === 'lobby'" class="title">Heist — type <b>!heist</b> to join the crew</span>
      <span v-else-if="phase === 'cancelled'" class="title">Heist — cancelled</span>
      <span v-else class="title">Heist — the getaway</span>
      <span v-if="phase !== 'cancelled'" class="odds">{{ Math.round(successChance) }}% escape</span>
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
</template>

<style scoped>
.nnz-heist {
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
.head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 10px; gap: 12px; }
.title { font-size: 20px; font-weight: 800; letter-spacing: 0.3px; }
.title b { color: var(--accent, #9146ff); }
.odds { font-size: 16px; font-weight: 800; color: var(--accent, #9146ff); white-space: nowrap; }
.roster { display: flex; flex-wrap: wrap; gap: 6px; }
.chip {
  font-size: 13px;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  color: rgba(255, 255, 255, 0.85);
}
.chip.escaped { color: #3ddc84; background: rgba(61, 220, 132, 0.14); }
.chip.caught { color: #ff6b6b; background: rgba(255, 107, 107, 0.12); }
</style>
