<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface LabelConfig { label: string; formatString: string; accentColor: string }
const cfg = reactive<LabelConfig>({ label: 'latest_follower', formatString: '', accentColor: '#9146ff' })

const raw = ref<string>('') // the tracked value (a name, or a count rendered as a string)
const cheerTotals: Record<string, number> = {}
let followCount = 0
let subCount = 0

const display = computed<string>(() => {
  if (!raw.value) return '—' // idle: never an error, just a placeholder until the first matching event
  return cfg.formatString ? cfg.formatString.replace(/\{value\}/g, raw.value) : raw.value
})

// A dashboard test fire (meta.test) is shown for a few seconds, then the real value comes back. It never
// changes the tracked value, a count or a total.
const PREVIEW_MS = 8000
let previewTimer: ReturnType<typeof setTimeout> | undefined
function preview(name: string): void {
  if (previewTimer === undefined) previewBase = raw.value
  raw.value = name
  clearTimeout(previewTimer)
  previewTimer = setTimeout(() => {
    raw.value = previewBase
    previewTimer = undefined
  }, PREVIEW_MS)
}
let previewBase = ''
function isTest(meta?: NnzEventMeta): boolean { return !!meta && !!meta.test }

function onFollow(d: NnzWidgetEventMap['follow'], _type?: string, meta?: NnzEventMeta): void {
  if (isTest(meta)) {
    if (cfg.label === 'latest_follower' && d && d.displayName) preview(d.displayName)
    return
  }
  // FollowAlertDto — camelCase on the wire: displayName, not user.
  followCount += 1
  if (cfg.label === 'latest_follower') raw.value = (d && d.displayName) || raw.value
  else if (cfg.label === 'follower_count') raw.value = String(followCount)
}
function onSub(d: NnzWidgetEventMap['subscription'], _type?: string, meta?: NnzEventMeta): void {
  if (isTest(meta)) {
    if (cfg.label === 'latest_sub' && d && d.displayName) preview(d.displayName)
    return
  }
  // SubscriptionAlertDto — camelCase on the wire: displayName, not user.
  subCount += 1
  if (cfg.label === 'latest_sub') raw.value = (d && d.displayName) || raw.value
  else if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onResub(d: NnzWidgetEventMap['resub'], _type?: string, meta?: NnzEventMeta): void {
  if (isTest(meta)) {
    if (cfg.label === 'latest_sub' && d && d.displayName) preview(d.displayName)
    return
  }
  // ResubAlertDto — camelCase on the wire: displayName, not user.
  if (cfg.label === 'latest_sub') raw.value = (d && d.displayName) || raw.value
}
function onGift(d: NnzWidgetEventMap['gift'], _type?: string, meta?: NnzEventMeta): void {
  if (isTest(meta)) return
  // GiftSubAlertDto — camelCase on the wire: count, not amount.
  const n: number = Math.max(1, Number(d && d.count) || 1)
  subCount += n
  if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onCheer(d: NnzWidgetEventMap['cheer'], _type?: string, meta?: NnzEventMeta): void {
  if (isTest(meta)) return
  // CheerAlertDto — camelCase on the wire: displayName/bits, not user/amount.
  if (cfg.label !== 'top_cheerer') return
  const user: string = (d && d.displayName) || ''
  if (!user) return
  cheerTotals[user] = (cheerTotals[user] || 0) + (Number(d && d.bits) || 0)
  let top = ''
  let best = -1
  Object.entries(cheerTotals).forEach(([u, total]) => {
    if (total > best) { best = total; top = u }
  })
  raw.value = top
}
// A goal event seeds the absolute count so the label reflects the real total, not just live deltas.
function onGoal(d: NnzWidgetEventMap['goal']): void {
  if (!d) return
  if (cfg.label === 'follower_count' && d.metric === 'followers' && isFinite(Number(d.value))) {
    followCount = Number(d.value)
    raw.value = String(followCount)
  }
  if (cfg.label === 'sub_count' && d.metric === 'subs' && isFinite(Number(d.value))) {
    subCount = Number(d.value)
    raw.value = String(subCount)
  }
}

// A count event carries the absolute total (the seed after an overlay reload); it is not a goal.
function onCount(d: NnzWidgetEventMap['count']): void {
  if (!d || !isFinite(Number(d.value))) return
  if (cfg.label === 'follower_count' && d.metric === 'followers') {
    followCount = Number(d.value)
    raw.value = String(followCount)
  }
  if (cfg.label === 'sub_count' && d.metric === 'subs') {
    subCount = Number(d.value)
    raw.value = String(subCount)
  }
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.label === 'string' && s.label) cfg.label = s.label
    if (typeof s.formatString === 'string') cfg.formatString = s.formatString
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('follow', onFollow)
  NomNomz.on('subscription', onSub)
  NomNomz.on('resub', onResub)
  NomNomz.on('gift', onGift)
  NomNomz.on('cheer', onCheer)
  NomNomz.on('goal', onGoal)
  NomNomz.on('count', onCount)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('follow', onFollow)
  NomNomz.off('subscription', onSub)
  NomNomz.off('resub', onResub)
  NomNomz.off('gift', onGift)
  NomNomz.off('cheer', onCheer)
  NomNomz.off('goal', onGoal)
  NomNomz.off('count', onCount)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the label. -->
  <div class="nnz-widget nnz-label">
    <span class="nnz-pill nnz-tilt nnz-enter"><span class="nnz-title value">{{ display }}</span></span>
  </div>
</template>

<style scoped>
.nnz-label {
  position: fixed;
  left: 24px;
  bottom: 24px;
}
.value {
  white-space: nowrap;
}
</style>
