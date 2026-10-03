<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
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

function onFollow(d: NnzWidgetEventMap['follow']): void {
  // FollowAlertDto — camelCase on the wire: displayName, not user.
  followCount += 1
  if (cfg.label === 'latest_follower') raw.value = (d && d.displayName) || raw.value
  else if (cfg.label === 'follower_count') raw.value = String(followCount)
}
function onSub(d: NnzWidgetEventMap['subscription']): void {
  // SubscriptionAlertDto — camelCase on the wire: displayName, not user.
  subCount += 1
  if (cfg.label === 'latest_sub') raw.value = (d && d.displayName) || raw.value
  else if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onResub(d: NnzWidgetEventMap['resub']): void {
  // ResubAlertDto — camelCase on the wire: displayName, not user.
  if (cfg.label === 'latest_sub') raw.value = (d && d.displayName) || raw.value
}
function onGift(d: NnzWidgetEventMap['gift']): void {
  // GiftSubAlertDto — camelCase on the wire: count, not amount.
  const n: number = Math.max(1, Number(d && d.count) || 1)
  subCount += n
  if (cfg.label === 'sub_count') raw.value = String(subCount)
}
function onCheer(d: NnzWidgetEventMap['cheer']): void {
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
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('follow', onFollow)
  NomNomz.off('subscription', onSub)
  NomNomz.off('resub', onResub)
  NomNomz.off('gift', onGift)
  NomNomz.off('cheer', onCheer)
  NomNomz.off('goal', onGoal)
})
</script>

<template>
  <div class="nnz-label" :style="{ '--accent': cfg.accentColor }">
    <span class="value">{{ display }}</span>
  </div>
</template>

<style scoped>
.nnz-label {
  position: fixed;
  left: 24px;
  bottom: 24px;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  color: #fff;
}
.value {
  display: inline-block;
  padding: 6px 14px;
  border-radius: 10px;
  font-size: 30px;
  font-weight: 800;
  letter-spacing: 0.3px;
  line-height: 1.1;
  color: var(--accent, #9146ff);
  background: rgba(12, 12, 18, 0.72);
  text-shadow: 0 2px 14px color-mix(in srgb, var(--accent, #9146ff) 50%, transparent);
}
</style>
