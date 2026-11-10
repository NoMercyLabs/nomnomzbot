<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

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
  cfg.handles[index.value % cfg.handles.length] ?? null,
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

// The JSON setting arrives as `unknown`: it must be an array before any entry is read.
function normalize(raw: unknown): Social[] {
  if (!Array.isArray(raw)) return []
  return raw
    .map((h: Partial<Social> | null | undefined) => ({ label: String((h && h.label) || ''), handle: String((h && h.handle) || '') }))
    .filter((h: Social) => h.handle.length > 0)
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
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
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the pill. -->
  <div class="nnz-widget nnz-socials">
    <Transition name="nnz" mode="out-in">
      <div v-if="current" :key="current.label + current.handle" class="nnz-pill nnz-tilt">
        <span v-if="current.label" class="nnz-chip label">{{ current.label }}</span>
        <span class="nnz-title handle">{{ current.handle }}</span>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.nnz-socials {
  position: fixed;
  right: 24px;
  bottom: 24px;
}
.label {
  margin-bottom: 0;
}
.handle {
  font-size: 20px;
  white-space: nowrap;
}
</style>
