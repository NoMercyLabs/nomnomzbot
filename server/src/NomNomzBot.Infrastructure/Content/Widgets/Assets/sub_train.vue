<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

const cfg = reactive({ windowMs: 300000, accentColor: '#9146ff' })

// Timestamps of recent sub contributions (a gift of N counts as N). The "train" is the count still
// inside the rolling window; it hides once the window empties.
const events = ref<number[]>([])
const now = ref<number>(Date.now())
let ticker: number | undefined

// A dashboard test fire is shown for a short time and never joins the real train.
const TEST_SHOW_MS = 10000
const testCount = ref<number>(0)
let testTimer: number | undefined

function showTest(n: number): void {
  testCount.value += Math.max(1, n)
  if (testTimer) clearTimeout(testTimer)
  testTimer = window.setTimeout(() => { testCount.value = 0 }, TEST_SHOW_MS)
}

const count = computed<number>(() => {
  const cutoff = now.value - cfg.windowMs
  return events.value.filter((t) => t >= cutoff).length + testCount.value
})
const visible = computed<boolean>(() => count.value > 0)

// A replayed event (seeded on join) keeps the time it really happened, so the train resumes with its true time left.
function stamp(meta?: NnzEventMeta): number {
  const at = meta && meta.replay && meta.occurredAt ? Date.parse(meta.occurredAt) : NaN
  return isFinite(at) ? at : Date.now()
}
function add(n: number, t: number): void {
  for (let i = 0; i < Math.max(1, n); i++) events.value.push(t)
  now.value = Date.now()
}
function onSub(_d: unknown, _type: string, meta?: NnzEventMeta): void {
  if (meta && meta.test) return showTest(1)
  add(1, stamp(meta))
}
// GiftSubAlertDto (AlertDtos.cs) — camelCase on the wire: count, not amount. A gift of N counts as N.
function onGift(d: NnzWidgetEventMap['gift'] | null | undefined, _type: string, meta?: NnzEventMeta): void {
  if (meta && meta.test) return showTest(Number(d && d.count) || 1)
  add(Math.max(1, Number(d && d.count) || 1), stamp(meta))
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.windowMs)) && Number(s.windowMs) > 0) cfg.windowMs = Number(s.windowMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('subscription', onSub)
  NomNomz.on('resub', onSub)
  NomNomz.on('gift', onGift)
  // Re-evaluate the window each second so the train cools down on its own.
  ticker = window.setInterval(() => {
    now.value = Date.now()
    const cutoff = now.value - cfg.windowMs
    const oldest: number | undefined = events.value[0]
    if (oldest !== undefined && oldest < cutoff) {
      events.value = events.value.filter((t) => t >= cutoff)
    }
  }, 1000)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('subscription', onSub)
  NomNomz.off('resub', onSub)
  NomNomz.off('gift', onGift)
  if (ticker) clearInterval(ticker)
  if (testTimer) clearTimeout(testTimer)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the bar. -->
  <div class="nnz-widget nnz-sub-train">
    <Transition name="nnz">
      <div v-if="visible" class="nnz-pill">
        <span class="nnz-sub label">Sub train. All aboard.</span>
        <span class="nnz-title nnz-accent-text count">×{{ count }}</span>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.nnz-sub-train {
  position: fixed;
  left: 50%;
  top: 24px;
  transform: translateX(-50%);
}
.label {
  white-space: nowrap;
}
.count {
  font-variant-numeric: tabular-nums;
}
</style>
