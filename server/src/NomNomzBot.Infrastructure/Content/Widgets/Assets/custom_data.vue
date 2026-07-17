<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Live value of a custom data source (custom-events.md): subscribes to "custom.<source>" and reads
// one named field from the event's extracted-fields map ({ fields: { bpm: "72", ... } } — the
// CustomDataReceivedEvent shape). A heart-rate gauge is this widget with source=heartrate, field=bpm,
// render=gauge. Re-subscribes live when the source setting changes. Idle placeholder until data arrives.
interface CustomDataConfig {
  source: string   // custom-data source name (the <name> in custom.<name>)
  field: string    // field to read; '' = first field in the payload
  render: string   // 'number' | 'gauge' | 'text'
  label: string
  min: number      // gauge floor
  max: number      // gauge ceiling
  accentColor: string
}

const cfg = reactive<CustomDataConfig>({
  source: 'heartrate',
  field: 'bpm',
  render: 'number',
  label: '',
  min: 0,
  max: 200,
  accentColor: '#9146ff',
})

const raw = ref<string>('') // idle until the first event; rendered as '—' meanwhile
let boundType = ''

function pickField(d: any): string {
  const fields: any = (d && d.fields && typeof d.fields === 'object') ? d.fields : d
  if (!fields || typeof fields !== 'object') return ''
  if (cfg.field && fields[cfg.field] != null) return String(fields[cfg.field])
  const keys: string[] = Object.keys(fields)
  return keys.length ? String(fields[keys[0]]) : ''
}

function onData(d: any): void {
  const value: string = pickField(d)
  if (value !== '') raw.value = value
}

// The subscription tracks the configured source: unbind the old "custom.<name>" and bind the new one.
function rebind(): void {
  if (!nnz) return
  const type: string = 'custom.' + cfg.source
  if (type === boundType) return
  if (boundType) nnz.off(boundType, onData)
  boundType = type
  nnz.on(boundType, onData)
  raw.value = ''
}

const numeric = computed<number>(() => Number(raw.value))

const gaugePct = computed<number>(() => {
  const span: number = cfg.max - cfg.min
  if (span <= 0 || !isFinite(numeric.value)) return 0
  return Math.max(0, Math.min(100, Math.round(((numeric.value - cfg.min) / span) * 100)))
})

const display = computed<string>(() => {
  if (raw.value === '') return '—'
  if (cfg.render === 'number' && isFinite(numeric.value)) return String(numeric.value)
  return raw.value
})

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.source === 'string' && s.source) cfg.source = s.source
    if (typeof s.field === 'string') cfg.field = s.field
    if (typeof s.render === 'string' && s.render) cfg.render = s.render
    if (typeof s.label === 'string') cfg.label = s.label
    if (isFinite(Number(s.min))) cfg.min = Number(s.min)
    if (isFinite(Number(s.max))) cfg.max = Number(s.max)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    rebind()
  })
  rebind()
})

onUnmounted(() => {
  if (!nnz) return
  if (boundType) nnz.off(boundType, onData)
})
</script>

<template>
  <div class="nnz-customdata" :style="{ '--accent': cfg.accentColor }">
    <div v-if="cfg.label" class="label">{{ cfg.label }}</div>
    <div v-if="cfg.render === 'gauge'" class="gauge">
      <div class="value">{{ display }}</div>
      <div class="track"><div class="fill" :style="{ width: gaugePct + '%' }"></div></div>
      <div class="bounds"><span>{{ cfg.min }}</span><span>{{ cfg.max }}</span></div>
    </div>
    <div v-else class="value" :class="{ text: cfg.render === 'text' }">{{ display }}</div>
  </div>
</template>

<style scoped>
.nnz-customdata {
  position: fixed;
  right: 16px;
  bottom: 16px;
  min-width: 160px;
  padding: 14px 18px;
  border-radius: 12px;
  text-align: center;
  color: #fff;
  background: rgba(12, 12, 18, 0.85);
  border: 1px solid color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  pointer-events: none;
}
.label {
  font-size: 12px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 1.5px;
  color: var(--accent, #9146ff);
  margin-bottom: 6px;
}
.value {
  font-size: 38px;
  font-weight: 800;
  line-height: 1;
  font-variant-numeric: tabular-nums;
  text-shadow: 0 1px 12px color-mix(in srgb, var(--accent, #9146ff) 40%, transparent);
}
.value.text {
  font-size: 20px;
  font-weight: 600;
  line-height: 1.3;
  word-break: break-word;
}
.gauge .value {
  margin-bottom: 8px;
}
.track {
  height: 8px;
  border-radius: 4px;
  background: rgba(255, 255, 255, 0.14);
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 4px;
  background: var(--accent, #9146ff);
  transition: width 0.4s ease;
}
.bounds {
  display: flex;
  justify-content: space-between;
  margin-top: 4px;
  font-size: 11px;
  opacity: 0.65;
  font-family: ui-monospace, monospace;
}
</style>
