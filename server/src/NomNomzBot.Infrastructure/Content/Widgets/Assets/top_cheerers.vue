<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface Cheerer { user: string; bits: number }

const cfg = reactive({ count: 5, title: 'Top cheerers', accentColor: '#9146ff' })

// Running bit totals per cheerer for the session — the same in-widget accumulation the labels
// widget uses for its single top_cheerer, surfaced here as a full ranked board.
const totals: Record<string, number> = {}
const board = ref<Cheerer[]>([])

const shown = computed<Cheerer[]>(() => board.value.slice(0, Math.max(1, cfg.count)))

function onCheer(d: any): void {
  const user: string = (d && d.user) || ''
  const bits: number = Number(d && d.amount) || 0
  if (!user || bits <= 0) return
  totals[user] = (totals[user] || 0) + bits
  board.value = Object.keys(totals)
    .map((u) => ({ user: u, bits: totals[u] }))
    .sort((a, b) => b.bits - a.bits)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.title === 'string') cfg.title = s.title
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('cheer', onCheer)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('cheer', onCheer)
})
</script>

<template>
  <div class="nnz-top-cheerers" :style="{ '--accent': cfg.accentColor }">
    <div class="title">{{ cfg.title }}</div>
    <div class="list">
      <div v-for="(c, i) in shown" :key="c.user" class="row">
        <span class="rank">{{ i + 1 }}</span>
        <span class="name">{{ c.user }}</span>
        <span class="bits">{{ c.bits }}</span>
      </div>
      <div v-if="shown.length === 0" class="row empty">No cheers yet…</div>
    </div>
  </div>
</template>

<style scoped>
.nnz-top-cheerers {
  position: fixed;
  right: 24px;
  top: 24px;
  width: min(300px, 40vw);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.72);
  border-radius: 12px;
  padding: 12px 14px;
}
.title {
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 1.2px;
  text-transform: uppercase;
  color: var(--accent, #9146ff);
  margin-bottom: 8px;
}
.list { display: grid; gap: 5px; }
.row { display: flex; align-items: center; gap: 8px; font-size: 15px; font-weight: 600; }
.row.empty { color: rgba(255, 255, 255, 0.5); font-weight: 500; font-size: 14px; }
.rank {
  width: 20px;
  text-align: center;
  font-weight: 900;
  color: var(--accent, #9146ff);
  flex: none;
}
.name { flex: 1; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.bits { font-weight: 800; color: var(--accent, #9146ff); }
</style>
