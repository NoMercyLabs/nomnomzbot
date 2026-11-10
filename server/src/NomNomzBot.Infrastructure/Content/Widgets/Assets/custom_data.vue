<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

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

// The event name is built at run time ("custom." + source), so the payload arrives as `unknown`:
// { fields: { ... } } or the bare fields map, narrowed to a record before any field is read.
function pickField(d: unknown): string {
  const payload: { fields?: unknown } | null = (typeof d === 'object' && d !== null) ? (d as { fields?: unknown }) : null
  const fields: unknown = (payload && payload.fields && typeof payload.fields === 'object') ? payload.fields : d
  if (!fields || typeof fields !== 'object') return ''
  const map: Record<string, unknown> = fields as Record<string, unknown>
  if (cfg.field && map[cfg.field] != null) return String(map[cfg.field])
  const keys: string[] = Object.keys(map)
  const first: string | undefined = keys[0]
  return first === undefined ? '' : String(map[first])
}

function onData(d: unknown): void {
  const value: string = pickField(d)
  if (value !== '') raw.value = value
}

// The subscription tracks the configured source: unbind the old "custom.<name>" and bind the new one.
function rebind(): void {
  if (typeof NomNomz === 'undefined') return
  const type: string = 'custom.' + cfg.source
  if (type === boundType) return
  if (boundType) NomNomz.off(boundType, onData)
  boundType = type
  NomNomz.on(boundType, onData)
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
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
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
  if (typeof NomNomz === 'undefined') return
  if (boundType) NomNomz.off(boundType, onData)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <div class="nnz-widget nnz-card nnz-customdata">
    <div v-if="cfg.label" class="nnz-chip">{{ cfg.label }}</div>
    <div v-if="cfg.render === 'gauge'" class="gauge">
      <div class="nnz-title value">{{ display }}</div>
      <div class="nnz-track"><div class="nnz-fill" :style="{ width: gaugePct + '%' }"></div></div>
      <div class="nnz-sub bounds"><span>{{ cfg.min }}</span><span>{{ cfg.max }}</span></div>
    </div>
    <div v-else class="nnz-title value" :class="{ text: cfg.render === 'text' }">{{ display }}</div>
  </div>
</template>

<style scoped>
.nnz-customdata {
  position: fixed;
  right: 16px;
  bottom: 16px;
  min-width: 160px;
  text-align: center;
  pointer-events: none;
}
.value {
  font-size: 38px;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}
.value.text {
  font-size: 20px;
  line-height: 1.3;
  word-break: break-word;
}
.gauge .value {
  margin-bottom: 8px;
}
.bounds {
  display: flex;
  justify-content: space-between;
  margin-top: 4px;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}
</style>
