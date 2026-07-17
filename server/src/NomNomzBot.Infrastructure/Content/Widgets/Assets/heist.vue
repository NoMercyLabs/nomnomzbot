<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface CrewMember { player: string; stake: number }
interface HeistResult { player: string; stake: number; escaped: boolean; payout: number }

const cfg = reactive({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved'>('lobby')
const successChance = ref<number>(0)
const crew = ref<CrewMember[]>([])
const results = ref<HeistResult[]>([])
let hideTimer: number | undefined

function reset(): void {
  successChance.value = 0
  crew.value = []
  results.value = []
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
}

function onFrame(d: any): void {
  if (!d || typeof d !== 'object') return
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    successChance.value = Number(d.successChance) || 0
    return
  }
  if (d.kind === 'join') {
    successChance.value = Number(d.successChance) || successChance.value
    crew.value = Array.isArray(d.crew) ? d.crew : crew.value
    return
  }
  if (d.kind === 'results') {
    phase.value = 'resolved'
    successChance.value = Number(d.successChance) || successChance.value
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
  <div v-if="visible" class="nnz-heist" :style="{ '--accent': cfg.accentColor }">
    <div class="head">
      <span v-if="phase === 'lobby'" class="title">Heist — type <b>!heist</b> to join the crew</span>
      <span v-else class="title">Heist — the getaway</span>
      <span class="odds">{{ Math.round(successChance) }}% escape</span>
    </div>
    <div class="roster">
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
