<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

interface Social { label: string; handle: string }

// A config-only rotating handles bar — no event feed. The streamer fills `handles` in the widget
// settings; the bar cross-fades through them at `rotateMs`.
const cfg = reactive<{ handles: Social[]; rotateMs: number; accentColor: string }>({
  handles: [],
  rotateMs: 8000,
  accentColor: '#9146ff',
})
const index = ref<number>(0)
let rotator: number | undefined

const current = computed<Social | null>(() =>
  cfg.handles.length > 0 ? cfg.handles[index.value % cfg.handles.length] : null,
)

function restartRotation(): void {
  if (rotator) clearInterval(rotator)
  index.value = 0
  if (cfg.handles.length > 1) {
    rotator = window.setInterval(() => {
      index.value = (index.value + 1) % cfg.handles.length
    }, Math.max(1500, cfg.rotateMs))
  }
}

function normalize(raw: any): Social[] {
  if (!Array.isArray(raw)) return []
  return raw
    .map((h: any) => ({ label: String((h && h.label) || ''), handle: String((h && h.handle) || '') }))
    .filter((h: Social) => h.handle.length > 0)
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if ('handles' in s) cfg.handles = normalize(s.handles)
    if (isFinite(Number(s.rotateMs)) && Number(s.rotateMs) > 0) cfg.rotateMs = Number(s.rotateMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    restartRotation()
  })
})

onUnmounted(() => {
  if (rotator) clearInterval(rotator)
})
</script>

<template>
  <div v-if="current" class="nnz-socials" :style="{ '--accent': cfg.accentColor }">
    <span v-if="current.label" class="label">{{ current.label }}</span>
    <span class="handle">{{ current.handle }}</span>
  </div>
</template>

<style scoped>
.nnz-socials {
  position: fixed;
  right: 24px;
  bottom: 24px;
  display: flex;
  align-items: baseline;
  gap: 10px;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.72);
  border-radius: 999px;
  padding: 8px 18px;
}
.label {
  font-size: 13px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 1px;
  color: var(--accent, #9146ff);
}
.handle { font-size: 18px; font-weight: 800; }
</style>
