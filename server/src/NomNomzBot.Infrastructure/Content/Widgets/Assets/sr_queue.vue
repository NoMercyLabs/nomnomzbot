<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

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

interface QueueItem { title: string; requestedBy: string; durationSec: number }

const items = ref<QueueItem[]>([])

function fmtDuration(sec: number): string {
  const s: number = Math.max(0, Math.floor(sec))
  const m: number = Math.floor(s / 60)
  const r: number = s % 60
  return m + ':' + (r < 10 ? '0' : '') + r
}

function onQueue(d: any): void {
  const raw: any[] = (d && Array.isArray(d.items)) ? d.items : []
  items.value = raw
    .map((it: any) => ({
      title: (it && it.title) || '',
      requestedBy: (it && it.requestedBy) || '',
      durationSec: Number(it && it.durationSec) || 0,
    }))
    .filter((it: QueueItem) => !!it.title)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.showRequester === 'boolean') cfg.showRequester = s.showRequester
    if (typeof s.showDuration === 'boolean') cfg.showDuration = s.showDuration
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('sr_queue', onQueue)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('sr_queue', onQueue)
})
</script>

<template>
  <div v-if="items.length" class="nnz-srqueue" :style="{ '--accent': cfg.accentColor }">
    <div class="header">Up Next</div>
    <div v-for="(it, i) in items.slice(0, cfg.count)" :key="i" class="row">
      <span class="pos">{{ i + 1 }}</span>
      <span class="title">{{ it.title }}</span>
      <span v-if="cfg.showRequester && it.requestedBy" class="req">{{ it.requestedBy }}</span>
      <span v-if="cfg.showDuration && it.durationSec > 0" class="dur">{{ fmtDuration(it.durationSec) }}</span>
    </div>
  </div>
</template>

<style scoped>
.nnz-srqueue {
  position: fixed;
  right: 16px;
  top: 16px;
  width: min(360px, 34vw);
  padding: 12px 14px;
  border-radius: 12px;
  color: #fff;
  background: rgba(12, 12, 18, 0.85);
  border: 1px solid color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
.header {
  font-size: 13px;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: var(--accent, #9146ff);
  margin-bottom: 8px;
}
.row {
  display: flex;
  align-items: baseline;
  gap: 8px;
  padding: 4px 0;
  font-size: 14px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
}
.row:first-of-type {
  border-top: none;
}
.pos {
  flex: none;
  width: 18px;
  font-weight: 700;
  color: var(--accent, #9146ff);
}
.title {
  flex: 1;
  min-width: 0;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.req {
  flex: none;
  max-width: 30%;
  font-size: 12px;
  opacity: 0.7;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dur {
  flex: none;
  font-size: 12px;
  font-family: ui-monospace, monospace;
  opacity: 0.8;
}
</style>
