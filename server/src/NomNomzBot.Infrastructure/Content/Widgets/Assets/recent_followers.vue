<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

const cfg = reactive({ count: 5, title: 'Recent followers', accentColor: '#9146ff' })
const followers = ref<string[]>([])

// Newest first, capped at the configured count — a persistent standings panel, not a one-shot alert.
const shown = computed<string[]>(() => followers.value.slice(0, Math.max(1, cfg.count)))

function onFollow(d: NnzWidgetEventMap['follow'] | null | undefined, _type?: string, meta?: NnzEventMeta): void {
  if (meta && meta.test) return // a dashboard test fire never joins the real list
  // FollowAlertDto (AlertDtos.cs) — camelCase on the wire: displayName, not user.
  const user: string = (d && d.displayName) || ''
  if (!user) return
  followers.value = [user, ...followers.value.filter((f) => f !== user)]
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.title === 'string') cfg.title = s.title
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('follow', onFollow)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('follow', onFollow)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the panel. -->
  <div class="nnz-widget nnz-card panel">
    <div class="nnz-chip">{{ cfg.title }}</div>
    <div>
      <div v-for="(name, i) in shown" :key="name" class="nnz-row" :class="{ 'is-first': i === 0 }">
        <span class="nnz-num">{{ i + 1 }}</span>
        <span class="nnz-grow">{{ name }}</span>
      </div>
      <div v-if="shown.length === 0" class="nnz-row nnz-sub">Nobody yet. Be the first.</div>
    </div>
  </div>
</template>

<style scoped>
.panel {
  position: fixed;
  left: 24px;
  top: 24px;
  width: min(300px, 40vw);
}
</style>
