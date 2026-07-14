<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface LabelConfig { label: string; formatString: string; accentColor: string }
const cfg = reactive<LabelConfig>({ label: 'latest_follower', formatString: '', accentColor: '#9146ff' })

const raw = ref<string>('') // the tracked value (a name, or a count rendered as a string)
const cheerTotals: Record<string, number> = {}
let followCount = 0
let subCount = 0

const display = computed<string>(() => {
  if (!raw.value) return '—' // idle: never an error, just a placeholder until the first matching event
  return cfg.formatString ? cfg.formatString.replace(/\{value\}/g, raw.value) : raw.value
})

function onFollow(d: any): void {
  followCount += 1
  if (cfg.label === 'latest_follower') raw.value = (d && d.user) || raw.value
  else if (cfg.label === 'follower_count') raw.value = String(followCount)
}
function onSub(d: any): void {
  subCount += 1
  if (cfg.label === 'latest_sub') raw.value = (d && d.user) || raw.value
  else if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onResub(d: any): void {
  if (cfg.label === 'latest_sub') raw.value = (d && d.user) || raw.value
}
function onGift(d: any): void {
  const n: number = Math.max(1, Number(d && d.amount) || 1)
  subCount += n
  if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onCheer(d: any): void {
  if (cfg.label !== 'top_cheerer') return
  const user: string = (d && d.user) || ''
  if (!user) return
  cheerTotals[user] = (cheerTotals[user] || 0) + (Number(d && d.amount) || 0)
  let top = ''
  let best = -1
  Object.keys(cheerTotals).forEach((u: string) => {
    if (cheerTotals[u] > best) { best = cheerTotals[u]; top = u }
  })
  raw.value = top
}
// A goal event seeds the absolute count so the label reflects the real total, not just live deltas.
function onGoal(d: any): void {
  if (!d) return
  if (cfg.label === 'follower_count' && d.metric === 'followers' && isFinite(Number(d.value))) {
    followCount = Number(d.value)
    raw.value = String(followCount)
  }
  if (cfg.label === 'sub_count' && d.metric === 'subs' && isFinite(Number(d.value))) {
    subCount = Number(d.value)
    raw.value = String(subCount)
  }
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.label === 'string' && s.label) cfg.label = s.label
    if (typeof s.formatString === 'string') cfg.formatString = s.formatString
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('follow', onFollow)
  nnz.on('subscription', onSub)
  nnz.on('resub', onResub)
  nnz.on('gift', onGift)
  nnz.on('cheer', onCheer)
  nnz.on('goal', onGoal)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('follow', onFollow)
  nnz.off('subscription', onSub)
  nnz.off('resub', onResub)
  nnz.off('gift', onGift)
  nnz.off('cheer', onCheer)
  nnz.off('goal', onGoal)
})
</script>

<template>
  <div class="nnz-label" :style="{ '--accent': cfg.accentColor }">
    <span class="value">{{ display }}</span>
  </div>
</template>

<style scoped>
.nnz-label {
  position: fixed;
  left: 24px;
  bottom: 24px;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
}
.value {
  display: inline-block;
  padding: 6px 14px;
  border-radius: 10px;
  font-size: 30px;
  font-weight: 800;
  letter-spacing: 0.3px;
  line-height: 1.1;
  color: var(--accent, #9146ff);
  background: rgba(12, 12, 18, 0.72);
  text-shadow: 0 2px 14px color-mix(in srgb, var(--accent, #9146ff) 50%, transparent);
}
</style>
