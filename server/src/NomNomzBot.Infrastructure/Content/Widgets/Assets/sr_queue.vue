<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Upcoming song-request queue. Driven by the "sr_queue" widget event — a full queue snapshot
// { items: [{ title, requestedBy, durationSec }] } (pushable today via the widget_event pipeline
// action; the music module's live queue feed binds the same shape when it ships). Hidden while empty.
interface SrQueueConfig {
  count: number
  showRequester: boolean
  showDuration: boolean
  accentColor: string
}

const cfg = reactive<SrQueueConfig>({
  count: 5,
  showRequester: true,
  showDuration: true,
  accentColor: '#9146ff',
})

interface QueueItem { title: string; requestedBy: string; durationSec: number; number: number; code: string }

const items = ref<QueueItem[]>([])

function fmtDuration(sec: number): string {
  const s: number = Math.max(0, Math.floor(sec))
  const m: number = Math.floor(s / 60)
  const r: number = s % 60
  return m + ':' + (r < 10 ? '0' : '') + r
}

type SrQueueItem = NnzWidgetEventMap['sr_queue']['items'][number]

function toItems(d: NnzWidgetEventMap['sr_queue'] | null | undefined): QueueItem[] {
  const raw: SrQueueItem[] = (d && Array.isArray(d.items)) ? d.items : []
  return raw
    .map((it: SrQueueItem | null | undefined) => ({
      title: (it && it.title) || '',
      requestedBy: (it && it.requestedBy) || '',
      durationSec: Number(it && it.durationSec) || 0,
      number: Number(it && it.number) || 0,
      code: (it && it.code) || '',
    }))
    .filter((it: QueueItem) => !!it.title)
}

// A dashboard test fire (meta.test) is shown for a few seconds, then the real queue comes back.
const PREVIEW_MS = 8000
let previewTimer: number | undefined
let realItems: QueueItem[] = []

function onQueue(d: NnzWidgetEventMap['sr_queue'] | null | undefined, _type?: string, meta?: NnzEventMeta): void {
  const next: QueueItem[] = toItems(d)
  if (meta && meta.test) {
    items.value = next
    if (previewTimer) window.clearTimeout(previewTimer)
    previewTimer = window.setTimeout(() => {
      items.value = realItems
      previewTimer = undefined
    }, PREVIEW_MS)
    return
  }
  realItems = next
  if (!previewTimer) items.value = next
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.showRequester === 'boolean') cfg.showRequester = s.showRequester
    if (typeof s.showDuration === 'boolean') cfg.showDuration = s.showDuration
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('sr_queue', onQueue)
})

onUnmounted(() => {
  if (previewTimer) window.clearTimeout(previewTimer)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('sr_queue', onQueue)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <Transition name="nnz">
    <div v-if="items.length" class="nnz-widget nnz-card nnz-srqueue">
      <div class="nnz-chip">Up Next</div>
      <div v-for="(it, i) in items.slice(0, cfg.count)" :key="i" class="nnz-row" :class="{ 'is-first': i === 0 }">
        <span class="nnz-num">#{{ it.number }}</span>
        <span v-if="it.code" class="code" title="Say this code to name this request, e.g. !wrongsong">{{ it.code }}</span>
        <span class="nnz-grow">{{ it.title }}</span>
        <span v-if="cfg.showRequester && it.requestedBy" class="nnz-end req">{{ it.requestedBy }}</span>
        <span v-if="cfg.showDuration && it.durationSec > 0" class="nnz-end dur">{{ fmtDuration(it.durationSec) }}</span>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.nnz-srqueue {
  position: fixed;
  right: 16px;
  top: 16px;
  width: min(360px, 34vw);
}
.code {
  flex: none;
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 1px;
  padding: 1px 6px;
  border-radius: var(--nnz-radius-inner);
  color: var(--nnz-text);
  background: var(--nnz-track);
}
.req {
  max-width: 30%;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dur {
  font-variant-numeric: tabular-nums;
}
</style>
