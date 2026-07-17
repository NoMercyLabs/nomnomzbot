<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

const cfg = reactive({ count: 5, title: 'Recent followers', accentColor: '#9146ff' })
const followers = ref<string[]>([])

// Newest first, capped at the configured count — a persistent standings panel, not a one-shot alert.
const shown = computed<string[]>(() => followers.value.slice(0, Math.max(1, cfg.count)))

function onFollow(d: any): void {
  const user: string = (d && d.user) || ''
  if (!user) return
  followers.value = [user, ...followers.value.filter((f) => f !== user)]
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.title === 'string') cfg.title = s.title
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('follow', onFollow)
})

onUnmounted(() => {
  if (!nnz) return
  nnz.off('follow', onFollow)
})
</script>

<template>
  <div class="nnz-recent-followers" :style="{ '--accent': cfg.accentColor }">
    <div class="title">{{ cfg.title }}</div>
    <div class="list">
      <div v-for="(name, i) in shown" :key="name" class="row" :class="{ latest: i === 0 }">
        <span class="dot"></span>
        <span class="name">{{ name }}</span>
      </div>
      <div v-if="shown.length === 0" class="row empty">Waiting for the first follow…</div>
    </div>
  </div>
</template>

<style scoped>
.nnz-recent-followers {
  position: fixed;
  left: 24px;
  top: 24px;
  width: min(300px, 40vw);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
  background: rgba(12, 12, 18, 0.72);
  border-radius: 12px;
  padding: 12px 14px;
}
.title {
  font-size: 13px;
  font-weight: 800;
  letter-spacing: 1.2px;
  text-transform: uppercase;
  color: var(--accent, #9146ff);
  margin-bottom: 8px;
}
.list { display: grid; gap: 5px; }
.row { display: flex; align-items: center; gap: 8px; font-size: 16px; font-weight: 600; }
.row.empty { color: rgba(255, 255, 255, 0.5); font-weight: 500; font-size: 14px; }
.row.latest .name { color: var(--accent, #9146ff); }
.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--accent, #9146ff);
  flex: none;
}
.name { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
</style>
