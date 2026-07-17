<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

// Channel-point redemption popup. Binds "reward_redeemed" (RewardRedeemedBroadcastHandler:
// RewardRedeemedDto — { rewardId, rewardTitle, userDisplayName, cost, userInput, avatarUrl, ... }).
// One card at a time, queued like the alerts widget.
interface RedemptionConfig {
  rewards: string[]     // per-reward enable: reward ids or titles; empty = every reward
  textTemplate: string  // {user} {reward} {cost} {input}; empty = default copy
  sound: string         // sound-clip key; playback rides the host audio bus (play_sound), never a widget fetch
  durationMs: number
  accentColor: string
}

const cfg = reactive<RedemptionConfig>({
  rewards: [],
  textTemplate: '',
  sound: '',
  durationMs: 6000,
  accentColor: '#9146ff',
})

interface RedemptionCard { user: string; reward: string; cost: number; input: string }

const queue: RedemptionCard[] = []
const current = ref<RedemptionCard | null>(null)
const visible = ref<boolean>(false)
const cardKey = ref<number>(0)
let timer: number | undefined

function enabled(d: any): boolean {
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

function onRedeemed(d: any): void {
  const data: any = d || {}
  if (!enabled(data)) return
  queue.push({
    user: data.userDisplayName || 'Someone',
    reward: data.rewardTitle || 'a reward',
    cost: Number(data.cost) || 0,
    input: data.userInput || '',
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
    window.setTimeout(showNext, 400)
  }, Math.max(1000, cfg.durationMs))
}

onMounted(() => {
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (Array.isArray(s.rewards)) cfg.rewards = s.rewards.slice()
    if (typeof s.textTemplate === 'string') cfg.textTemplate = s.textTemplate
    if (typeof s.sound === 'string') cfg.sound = s.sound
    if (isFinite(Number(s.durationMs))) cfg.durationMs = Number(s.durationMs)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  nnz.on('reward_redeemed', onRedeemed)
})

onUnmounted(() => {
  if (timer) window.clearTimeout(timer)
  if (!nnz) return
  nnz.off('reward_redeemed', onRedeemed)
})
</script>

<template>
  <div class="nnz-redemption" :style="{ '--accent': cfg.accentColor }">
    <div v-if="current" :key="cardKey" class="card" :class="{ show: visible }">
      <div class="badge">&#127873;</div>
      <div class="title">{{ headline(current) }}</div>
      <div v-if="current.cost > 0" class="cost">{{ current.cost }} points</div>
      <div v-if="current.input" class="input">&ldquo;{{ current.input }}&rdquo;</div>
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
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
.card {
  min-width: 280px;
  max-width: 70vw;
  padding: 20px 36px;
  border-radius: 16px;
  text-align: center;
  color: #fff;
  background: rgba(12, 12, 18, 0.86);
  border: 2px solid var(--accent, #9146ff);
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.55),
    0 0 24px color-mix(in srgb, var(--accent, #9146ff) 35%, transparent);
  opacity: 0;
  transform: translateY(-18px) scale(0.96);
  transition: opacity 0.35s ease, transform 0.35s cubic-bezier(0.22, 1, 0.36, 1);
}
.card.show {
  opacity: 1;
  transform: translateY(0) scale(1);
}
.badge {
  font-size: 30px;
  line-height: 1;
  margin-bottom: 8px;
}
.title {
  font-size: 24px;
  font-weight: 800;
  letter-spacing: 0.2px;
  color: var(--accent, #9146ff);
  text-shadow: 0 1px 12px color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
}
.cost {
  margin-top: 6px;
  font-size: 14px;
  font-weight: 700;
  opacity: 0.85;
  text-transform: uppercase;
  letter-spacing: 1px;
}
.input {
  margin-top: 8px;
  font-size: 16px;
  font-style: italic;
  opacity: 0.92;
  word-break: break-word;
}
</style>
