<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface Cheerer { user: string; bits: number }

const cfg = reactive({ count: 5, title: 'Top cheerers', accentColor: '#9146ff' })

// Running bit totals per cheerer for the session — the same in-widget accumulation the labels
// widget uses for its single top_cheerer, surfaced here as a full ranked board.
const totals: Record<string, number> = {}
const board = ref<Cheerer[]>([])

const shown = computed<Cheerer[]>(() => board.value.slice(0, Math.max(1, cfg.count)))

function onCheer(d: NnzWidgetEventMap['cheer'] | null | undefined, _type?: string, meta?: NnzEventMeta): void {
  if (meta && meta.test) return // a dashboard test fire never counts toward the real board
  // CheerAlertDto (AlertDtos.cs) — camelCase on the wire: displayName/bits, not user/amount.
  const user: string = (d && d.displayName) || ''
  const bits: number = Number(d && d.bits) || 0
  if (!user || bits <= 0) return
  totals[user] = (totals[user] || 0) + bits
  board.value = Object.entries(totals)
    .map(([user, bits]) => ({ user, bits }))
    .sort((a, b) => b.bits - a.bits)
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.title === 'string') cfg.title = s.title
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('cheer', onCheer)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('cheer', onCheer)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <div class="nnz-widget nnz-card nnz-top-cheerers">
    <div class="nnz-chip">{{ cfg.title }}</div>
    <TransitionGroup name="nnz" tag="div">
      <div v-for="(c, i) in shown" :key="c.user" class="nnz-row" :class="{ 'is-first': i === 0 }">
        <span class="nnz-num">{{ i + 1 }}</span>
        <span class="nnz-grow">{{ c.user }}</span>
        <span class="nnz-end">{{ c.bits }}</span>
      </div>
    </TransitionGroup>
    <div v-if="shown.length === 0" class="nnz-row nnz-sub">No bits yet. Your move.</div>
  </div>
</template>

<style scoped>
.nnz-top-cheerers {
  position: fixed;
  right: 24px;
  top: 24px;
  width: min(300px, 40vw);
}
</style>
