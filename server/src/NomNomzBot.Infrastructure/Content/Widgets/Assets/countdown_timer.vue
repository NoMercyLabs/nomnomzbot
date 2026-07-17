<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Countdown to a wall-clock target or for a fixed duration (BRB / starting-soon). Entirely
// settings-driven: the dashboard controls it live through widget-settings saves (WidgetSettingsChanged
// → onSettings re-arms the countdown) — no event feed needed. `target` (ISO date-time) wins over
// `durationMs`, which (re)starts whenever it changes.
interface CountdownConfig {
  target: string        // ISO date-time; '' = use durationMs
  durationMs: number    // countdown length when no target is set; 0 = idle
  label: string
  onCompleteText: string
  accentColor: string
}

const cfg = reactive<CountdownConfig>({
  target: '',
  durationMs: 0,
  label: 'Starting soon',
  onCompleteText: '',
  accentColor: '#9146ff',
})

const remainingMs = ref<number>(-1) // -1 = idle (nothing configured), 0 = complete
let endAt = 0
let armedKey = ''
let tick: number | undefined

function fmt(ms: number): string {
  const total: number = Math.max(0, Math.ceil(ms / 1000))
  const h: number = Math.floor(total / 3600)
  const m: number = Math.floor((total % 3600) / 60)
  const s: number = total % 60
  const mm: string = (m < 10 && h > 0 ? '0' : '') + m
  const ss: string = (s < 10 ? '0' : '') + s
  return (h > 0 ? h + ':' : '') + mm + ':' + ss
}

// Re-arm only when the target/duration pair actually changes, so unrelated settings saves
// (label, colour) never restart a running countdown.
function arm(): void {
  const key: string = cfg.target + '|' + cfg.durationMs
  if (key === armedKey) return
  armedKey = key
  if (cfg.target) {
    const t: number = Date.parse(cfg.target)
    endAt = isFinite(t) ? t : 0
  } else if (cfg.durationMs > 0) {
    endAt = Date.now() + cfg.durationMs
  } else {
    endAt = 0
  }
  remainingMs.value = endAt > 0 ? Math.max(0, endAt - Date.now()) : -1
}

onMounted(() => {
  tick = window.setInterval(() => {
    if (endAt > 0) remainingMs.value = Math.max(0, endAt - Date.now())
  }, 250)
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.target === 'string') cfg.target = s.target
    if (isFinite(Number(s.durationMs)) && Number(s.durationMs) >= 0) cfg.durationMs = Number(s.durationMs)
    if (typeof s.label === 'string') cfg.label = s.label
    if (typeof s.onCompleteText === 'string') cfg.onCompleteText = s.onCompleteText
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    arm()
  })
})

onUnmounted(() => {
  if (tick) window.clearInterval(tick)
})
</script>

<template>
  <div v-if="remainingMs >= 0" class="nnz-countdown" :style="{ '--accent': cfg.accentColor }">
    <div v-if="cfg.label" class="label">{{ cfg.label }}</div>
    <div v-if="remainingMs > 0" class="time">{{ fmt(remainingMs) }}</div>
    <div v-else class="done">{{ cfg.onCompleteText || '00:00' }}</div>
  </div>
</template>

<style scoped>
.nnz-countdown {
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  min-width: 220px;
  padding: 22px 40px;
  border-radius: 16px;
  text-align: center;
  color: #fff;
  background: rgba(12, 12, 18, 0.86);
  border: 2px solid var(--accent, #9146ff);
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.55),
    0 0 24px color-mix(in srgb, var(--accent, #9146ff) 35%, transparent);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  pointer-events: none;
}
.label {
  font-size: 15px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 2px;
  color: var(--accent, #9146ff);
  margin-bottom: 8px;
}
.time {
  font-size: 52px;
  font-weight: 800;
  font-variant-numeric: tabular-nums;
  font-family: ui-monospace, 'Cascadia Mono', monospace;
  line-height: 1;
  text-shadow: 0 1px 12px color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
}
.done {
  font-size: 32px;
  font-weight: 800;
  line-height: 1.2;
  color: var(--accent, #9146ff);
}
</style>
