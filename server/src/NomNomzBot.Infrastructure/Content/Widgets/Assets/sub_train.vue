<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

const cfg = reactive({ windowMs: 300000, accentColor: '#9146ff' })

// Timestamps of recent sub contributions (a gift of N counts as N). The "train" is the count still
// inside the rolling window; it hides once the window empties.
const events = ref<number[]>([])
const now = ref<number>(Date.now())
let ticker: number | undefined

const count = computed<number>(() => {
  const cutoff = now.value - cfg.windowMs
  return events.value.filter((t) => t >= cutoff).length
})
const visible = computed<boolean>(() => count.value > 0)

function add(n: number): void {
  const t = Date.now()
  for (let i = 0; i < Math.max(1, n); i++) events.value.push(t)
  now.value = t
}
function onSub(): void { add(1) }
function onGift(d: any): void { add(Math.max(1, Number(d && d.amount) || 1)) }

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.windowMs)) && Number(s.windowMs) > 0) cfg.windowMs = Number(s.windowMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('subscription', onSub)
  nnz.on('resub', onSub)
  nnz.on('gift', onGift)
  // Re-evaluate the window each second so the train cools down on its own.
  ticker = window.setInterval(() => {
    now.value = Date.now()
    const cutoff = now.value - cfg.windowMs
    if (events.value.length && events.value[0] < cutoff) {
      events.value = events.value.filter((t) => t >= cutoff)
    }
  }, 1000)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('subscription', onSub)
  nnz.off('resub', onSub)
  nnz.off('gift', onGift)
  if (ticker) clearInterval(ticker)
})
</script>

<template>
  <div v-if="visible" class="nnz-sub-train" :style="{ '--accent': cfg.accentColor }">
    <span class="engine">🚂</span>
    <span class="label">SUB TRAIN</span>
    <span class="count">×{{ count }}</span>
  </div>
</template>

<style scoped>
.nnz-sub-train {
  position: fixed;
  left: 50%;
  top: 24px;
  transform: translateX(-50%);
  display: flex;
  align-items: center;
  gap: 12px;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.8);
  border-radius: 999px;
  padding: 10px 22px;
  box-shadow: 0 4px 24px color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
}
.engine { font-size: 24px; }
.label { font-size: 18px; font-weight: 900; letter-spacing: 2px; }
.count {
  font-size: 26px;
  font-weight: 900;
  color: var(--accent, #9146ff);
  text-shadow: 0 2px 14px color-mix(in srgb, var(--accent, #9146ff) 60%, transparent);
}
</style>
