<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

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

// The colors and labels settings are free-form JSON (typed unknown); this narrows one to its object shape.
function isObject<T extends object>(v: unknown): v is T {
  return !!v && typeof v === 'object'
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
function onGoal(d: NnzWidgetEventMap['goal']): void {
  if (!d || d.metric !== cfg.metric) return
  if (isFinite(Number(d.value))) value.value = Number(d.value)
  if (isFinite(Number(d.target)) && Number(d.target) > 0) cfg.target = Number(d.target)
}
function onFollow(): void { if (cfg.metric === 'followers') value.value += 1 }
function onSub(): void { if (cfg.metric === 'subs') value.value += 1 }
// GiftSubAlertDto — camelCase on the wire: count, not amount.
function onGift(d: NnzWidgetEventMap['gift']): void { if (cfg.metric === 'subs') value.value += Math.max(1, Number(d && d.count) || 1) }
// CheerAlertDto — camelCase on the wire: bits, not amount.
function onCheer(d: NnzWidgetEventMap['cheer']): void { if (cfg.metric === 'bits') value.value += Math.max(0, Number(d && d.bits) || 0) }

onMounted(() => {
  value.value = cfg.start
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.metric === 'string' && s.metric) cfg.metric = s.metric
    if (isFinite(Number(s.target))) cfg.target = Number(s.target)
    if (isFinite(Number(s.start))) {
      cfg.start = Number(s.start)
      if (value.value < cfg.start) value.value = cfg.start
    }
    if (typeof s.resetCadence === 'string') cfg.resetCadence = s.resetCadence
    if (isObject<GoalColors>(s.colors)) cfg.colors = s.colors
    if (isObject<GoalLabels>(s.labels)) cfg.labels = s.labels
  })
  NomNomz.on('goal', onGoal)
  NomNomz.on('follow', onFollow)
  NomNomz.on('subscription', onSub)
  NomNomz.on('gift', onGift)
  NomNomz.on('cheer', onCheer)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('goal', onGoal)
  NomNomz.off('follow', onFollow)
  NomNomz.off('subscription', onSub)
  NomNomz.off('gift', onGift)
  NomNomz.off('cheer', onCheer)
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
