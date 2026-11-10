<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Channel-point redemption popup. Binds "reward_redeemed" (RewardRedeemedBroadcastHandler:
// RewardRedeemedDto — { rewardId, rewardTitle, userDisplayName, cost, userInput, avatarUrl, ... }).
// One card at a time, queued like the alerts widget.
// `soundClipId` is display-only config here: RewardRedeemedBroadcastHandler resolves the configured clip and
// pushes a real PlaySound to the shared overlay audio bus alongside the WidgetEvent (S058b) — the SDK's audio
// bus already plays any PlaySound broadcast (see overlay/sdk.js), so this widget does not need to touch
// <audio> itself. Blank = the handler skips playback entirely; no client-side fallback is needed.
interface RedemptionConfig {
  rewards: string[]     // per-reward enable: reward ids or titles; empty = every reward
  textTemplate: string  // {user} {reward} {cost} {input}; empty = default copy
  durationMs: number
  soundClipId: string
  accentColor: string
}

const cfg = reactive<RedemptionConfig>({
  rewards: [],
  textTemplate: '',
  durationMs: 6000,
  soundClipId: '',
  accentColor: '#9146ff',
})

interface RedemptionCard { user: string; reward: string; cost: number; input: string; avatarUrl: string }

const queue: RedemptionCard[] = []
const current = ref<RedemptionCard | null>(null)
const visible = ref<boolean>(false)
const cardKey = ref<number>(0)
let timer: number | undefined

function enabled(d: Partial<NnzWidgetEventMap['reward_redeemed']>): boolean {
  if (!cfg.rewards.length) return true
  const id: string = (d && d.rewardId) || ''
  const title: string = (d && d.rewardTitle) || ''
  return cfg.rewards.indexOf(id) !== -1 || cfg.rewards.indexOf(title) !== -1
}

function headline(card: RedemptionCard): string {
  if (cfg.textTemplate)
    return cfg.textTemplate
      .replace(/\{user\}/g, card.user)
      .replace(/\{reward\}/g, card.reward)
      .replace(/\{cost\}/g, String(card.cost))
      .replace(/\{input\}/g, card.input)
  return card.user + ' redeemed ' + card.reward + '!'
}

function onRedeemed(data: NnzWidgetEventMap['reward_redeemed'] | null | undefined): void {
  const d: Partial<NnzWidgetEventMap['reward_redeemed']> = data || {}
  if (!enabled(d)) return
  queue.push({
    user: d.userDisplayName || 'Someone',
    reward: d.rewardTitle || 'a reward',
    cost: Number(d.cost) || 0,
    input: d.userInput || '',
    avatarUrl: d.avatarUrl || '',
  })
  if (!current.value) showNext()
}

// One card at a time: enter (next frame → .show), hold for durationMs, exit, then the next after the fade.
function showNext(): void {
  const next: RedemptionCard | undefined = queue.shift()
  if (!next) { current.value = null; return }
  current.value = next
  cardKey.value += 1
  visible.value = false
  requestAnimationFrame(() => { visible.value = true })
  timer = window.setTimeout(() => {
    visible.value = false
    timer = window.setTimeout(showNext, 400)
  }, Math.max(1000, cfg.durationMs))
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (Array.isArray(s.rewards)) cfg.rewards = s.rewards.slice()
    if (typeof s.textTemplate === 'string') cfg.textTemplate = s.textTemplate
    if (isFinite(Number(s.durationMs))) cfg.durationMs = Number(s.durationMs)
    if (typeof s.soundClipId === 'string') cfg.soundClipId = s.soundClipId
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('reward_redeemed', onRedeemed)
})

onUnmounted(() => {
  if (timer) window.clearTimeout(timer)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('reward_redeemed', onRedeemed)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the card. -->
  <div class="nnz-widget nnz-redemption">
    <div v-if="current" :key="cardKey" class="nnz-pill nnz-tilt card" :class="{ show: visible }">
      <span class="nnz-face"></span>
      <img v-if="current.avatarUrl" class="nnz-avatar" :src="current.avatarUrl" alt="">
      <div class="body">
        <div class="nnz-title">{{ headline(current) }}</div>
        <div v-if="current.cost > 0" class="nnz-sub">{{ current.cost }} points</div>
        <div v-if="current.input" class="nnz-sub input">&ldquo;{{ current.input }}&rdquo;</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.nnz-redemption {
  position: fixed;
  top: 12%;
  left: 50%;
  transform: translateX(-50%);
  pointer-events: none;
}
/* .show is the widget's own in/out switch (driven by `visible`), timed with the shared theme motion. */
.card {
  min-width: 280px;
  max-width: 70vw;
  opacity: 0;
  transform: translateY(-18px) scale(0.96);
  transition: opacity var(--nnz-out) ease-in, transform var(--nnz-in) var(--nnz-ease);
}
.card.show {
  opacity: 1;
  transform: translateY(0) scale(1);
}
.body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.input {
  font-style: italic;
  word-break: break-word;
}
</style>
