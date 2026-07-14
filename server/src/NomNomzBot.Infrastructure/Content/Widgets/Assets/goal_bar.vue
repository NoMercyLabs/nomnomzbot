<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface GoalColors { bar?: string; track?: string; text?: string }
interface GoalLabels { title?: string }
interface GoalConfig {
  metric: string
  target: number
  start: number
  resetCadence: string
  colors: GoalColors
  labels: GoalLabels
}

const cfg = reactive<GoalConfig>({
  metric: 'followers',
  target: 100,
  start: 0,
  resetCadence: '',
  colors: {},
  labels: {},
})

const value = ref<number>(0)

const pct = computed<number>(() => {
  const span: number = cfg.target - cfg.start
  if (span <= 0) return 0
  const p: number = ((value.value - cfg.start) / span) * 100
  return Math.max(0, Math.min(100, Math.round(p)))
})

function defaultTitle(metric: string): string {
  if (metric === 'subs') return 'Sub Goal'
  if (metric === 'bits') return 'Bits Goal'
  return 'Follower Goal'
}
const title = computed<string>(() => cfg.labels.title || defaultTitle(cfg.metric))

// A goal event carrying our metric is the authoritative value; matching count events live-increment between them.
function onGoal(d: any): void {
  if (!d || d.metric !== cfg.metric) return
  if (isFinite(Number(d.value))) value.value = Number(d.value)
  if (isFinite(Number(d.target)) && Number(d.target) > 0) cfg.target = Number(d.target)
}
function onFollow(): void { if (cfg.metric === 'followers') value.value += 1 }
function onSub(): void { if (cfg.metric === 'subs') value.value += 1 }
function onGift(d: any): void { if (cfg.metric === 'subs') value.value += Math.max(1, Number(d && d.amount) || 1) }
function onCheer(d: any): void { if (cfg.metric === 'bits') value.value += Math.max(0, Number(d && d.amount) || 0) }

onMounted(() => {
  value.value = cfg.start
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.metric === 'string' && s.metric) cfg.metric = s.metric
    if (isFinite(Number(s.target))) cfg.target = Number(s.target)
    if (isFinite(Number(s.start))) {
      cfg.start = Number(s.start)
      if (value.value < cfg.start) value.value = cfg.start
    }
    if (typeof s.resetCadence === 'string') cfg.resetCadence = s.resetCadence
    if (s.colors && typeof s.colors === 'object') cfg.colors = s.colors
    if (s.labels && typeof s.labels === 'object') cfg.labels = s.labels
  })
  nnz.on('goal', onGoal)
  nnz.on('follow', onFollow)
  nnz.on('subscription', onSub)
  nnz.on('gift', onGift)
  nnz.on('cheer', onCheer)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('goal', onGoal)
  nnz.off('follow', onFollow)
  nnz.off('subscription', onSub)
  nnz.off('gift', onGift)
  nnz.off('cheer', onCheer)
})
</script>

<template>
  <div
    class="nnz-goal"
    :style="{
      '--bar': cfg.colors.bar || '#9146ff',
      '--track': cfg.colors.track || 'rgba(255,255,255,0.14)',
      '--text': cfg.colors.text || '#ffffff',
    }"
  >
    <div class="row">
      <span class="title">{{ title }}</span>
      <span class="count">{{ value }} / {{ cfg.target }}</span>
    </div>
    <div class="bar"><div class="fill" :style="{ width: pct + '%' }"></div></div>
    <div class="foot">
      <span class="pct">{{ pct }}%</span>
      <span v-if="cfg.resetCadence" class="cadence">{{ cfg.resetCadence }}</span>
    </div>
  </div>
</template>

<style scoped>
.nnz-goal {
  position: fixed;
  left: 50%;
  bottom: 40px;
  transform: translateX(-50%);
  width: min(520px, 80vw);
  padding: 16px 18px;
  border-radius: 14px;
  background: rgba(12, 12, 18, 0.82);
  color: var(--text, #fff);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.45);
}
.row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 10px;
}
.title {
  font-size: 17px;
  font-weight: 700;
  letter-spacing: 0.2px;
}
.count {
  font-size: 15px;
  font-weight: 600;
  opacity: 0.9;
  font-variant-numeric: tabular-nums;
}
.bar {
  height: 14px;
  border-radius: 999px;
  background: var(--track, rgba(255, 255, 255, 0.14));
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 999px;
  background-color: var(--bar, #9146ff);
  transition: width 0.6s cubic-bezier(0.22, 1, 0.36, 1);
  box-shadow: 0 0 12px color-mix(in srgb, var(--bar, #9146ff) 55%, transparent);
}
.foot {
  display: flex;
  justify-content: space-between;
  margin-top: 8px;
  font-size: 13px;
  opacity: 0.75;
}
.pct {
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.cadence {
  text-transform: uppercase;
  letter-spacing: 0.6px;
}
</style>
