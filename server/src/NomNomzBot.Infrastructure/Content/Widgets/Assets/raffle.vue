<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface Entrant { player: string; stake: number }
interface RaffleResult { player: string; stake: number; won: boolean; payout: number }

const cfg = reactive({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved'>('lobby')
const pot = ref<number>(0)
const entrants = ref<Entrant[]>([])
const results = ref<RaffleResult[]>([])
const winner = ref<string>('')
let hideTimer: number | undefined

function reset(): void {
  pot.value = 0
  entrants.value = []
  results.value = []
  winner.value = ''
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

function onFrame(d: any): void {
  if (!d || typeof d !== 'object') return
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    return
  }
  if (d.kind === 'join') {
    pot.value = Number(d.pot) || pot.value
    entrants.value = Array.isArray(d.entrants) ? d.entrants : entrants.value
    return
  }
  if (d.kind === 'results') {
    phase.value = 'resolved'
    pot.value = Number(d.pot) || pot.value
    winner.value = String(d.winner || '')
    results.value = Array.isArray(d.results) ? d.results : []
    scheduleHide()
  }
}

function scheduleHide(): void {
  if (hideTimer) clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => { visible.value = false }, cfg.hideAfterMs)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (isFinite(Number(s.hideAfterMs)) && Number(s.hideAfterMs) > 0) cfg.hideAfterMs = Number(s.hideAfterMs)
  })
  nnz.on('game.lobby', onFrame)
  nnz.on('game.running', onFrame)
  nnz.on('game.resolved', onFrame)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('game.lobby', onFrame)
  nnz.off('game.running', onFrame)
  nnz.off('game.resolved', onFrame)
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <div v-if="visible" class="nnz-raffle" :style="{ '--accent': cfg.accentColor }">
    <div class="head">
      <span v-if="phase === 'lobby'" class="title">Raffle — type <b>!raffle</b> to enter</span>
      <span v-else class="title">Raffle — winner</span>
      <span class="pot">Pot {{ pot }}</span>
    </div>
    <div v-if="phase === 'resolved'" class="winner">🎉 {{ winner }} takes {{ pot }}</div>
    <div class="roster">
      <template v-if="phase === 'lobby'">
        <span v-for="e in entrants" :key="e.player" class="chip">{{ e.player }} · {{ e.stake }}</span>
      </template>
      <template v-else>
        <span v-for="r in results" :key="r.player" class="chip" :class="{ won: r.won }">
          {{ r.player }}<span v-if="r.won"> +{{ r.payout }}</span>
        </span>
      </template>
    </div>
  </div>
</template>

<style scoped>
.nnz-raffle {
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
.head { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 10px; }
.title { font-size: 20px; font-weight: 800; letter-spacing: 0.3px; }
.title b { color: var(--accent, #9146ff); }
.pot { font-size: 16px; font-weight: 800; color: var(--accent, #9146ff); }
.winner {
  font-size: 22px;
  font-weight: 800;
  color: #3ddc84;
  margin-bottom: 10px;
}
.roster { display: flex; flex-wrap: wrap; gap: 6px; }
.chip {
  font-size: 13px;
  font-weight: 600;
  padding: 4px 10px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  color: rgba(255, 255, 255, 0.85);
}
.chip.won { color: #3ddc84; background: rgba(61, 220, 132, 0.14); }
</style>
