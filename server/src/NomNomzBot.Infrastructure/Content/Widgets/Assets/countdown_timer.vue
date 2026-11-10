<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Countdown to a wall-clock target or for a fixed duration (BRB / starting-soon). Entirely
// settings-driven: the dashboard controls it live through widget-settings saves (WidgetSettingsChanged
// → onSettings re-arms the countdown) — no event feed needed. `target` (ISO date-time) wins over
// `durationMs`, which (re)starts whenever it changes.
// target is an ISO date-time ('' = use durationMs); durationMs is the length when no target is set (0 = idle).
const cfg = reactive<NnzWidgetSettings>({
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
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
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
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the card. -->
  <div v-if="remainingMs >= 0" class="nnz-widget nnz-card nnz-enter nnz-countdown">
    <div v-if="cfg.label" class="nnz-chip">{{ cfg.label }}</div>
    <div v-if="remainingMs > 0" class="nnz-title time">{{ fmt(remainingMs) }}</div>
    <div v-else class="nnz-title nnz-accent-text done">{{ cfg.onCompleteText || '00:00' }}</div>
  </div>
</template>

<style scoped>
.nnz-countdown {
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  min-width: 220px;
  text-align: center;
  pointer-events: none;
}
.time {
  font-size: 52px;
  font-variant-numeric: tabular-nums;
  line-height: 1;
}
.done {
  font-size: 32px;
  line-height: 1.2;
}
</style>
