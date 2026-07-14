<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Its type does not cross files, so
// keep it loose. All event/settings wiring happens in onMounted; nothing here touches the DOM at module load.
const nnz = (window as any).NomNomz

const ALL_EVENTS: string[] = [
  'follow', 'subscription', 'resub', 'gift', 'cheer', 'raid',
  'supporter.tip', 'supporter.membership', 'supporter.merch', 'supporter.charity',
]

interface AlertConfig {
  events: string[]
  textTemplate: string
  durationMs: number
  minBits: number
  minGiftCount: number
  minAmount: number
  accentColor: string
}

const cfg = reactive<AlertConfig>({
  events: ALL_EVENTS.slice(),
  textTemplate: '',
  durationMs: 6000,
  minBits: 0,
  minGiftCount: 0,
  minAmount: 0,
  accentColor: '#9146ff',
})

interface AlertCard { title: string; detail: string }

const queue: AlertCard[] = []
const current = ref<AlertCard | null>(null)
const visible = ref<boolean>(false)
const cardKey = ref<number>(0)
let timer: number | undefined

function tierText(tier: string | undefined): string {
  if (tier === '2000') return 'Tier 2'
  if (tier === '3000') return 'Tier 3'
  return tier ? 'Tier 1' : ''
}

function money(d: any): string {
  const amount: number = Number(d.amount) || 0
  const currency: string = d.currency || ''
  return currency ? amount + ' ' + currency : String(amount)
}

function enabled(type: string): boolean {
  return cfg.events.indexOf(type) !== -1
}

// Amount gates keep small/spammy events off the overlay: bits for cheers, sub count for gifts, money for tips.
function passesThreshold(type: string, d: any): boolean {
  if (type === 'cheer') return (Number(d.amount) || 0) >= cfg.minBits
  if (type === 'gift') return (Number(d.amount) || 0) >= cfg.minGiftCount
  if (type.indexOf('supporter.') === 0) return (Number(d.amount) || 0) >= cfg.minAmount
  return true
}

function applyTemplate(d: any): string {
  return cfg.textTemplate
    .replace(/\{user\}/g, d.user || '')
    .replace(/\{amount\}/g, String(d.amount ?? ''))
    .replace(/\{tier\}/g, tierText(d.tier))
    .replace(/\{months\}/g, String(d.months ?? ''))
    .replace(/\{viewers\}/g, String(d.viewers ?? ''))
}

function cardFor(type: string, d: any): AlertCard | null {
  if (cfg.textTemplate) return { title: applyTemplate(d), detail: '' }
  const user: string = d.user || 'Someone'
  switch (type) {
    case 'follow': return { title: user + ' just followed!', detail: '' }
    case 'subscription': return { title: user + ' just subscribed!', detail: tierText(d.tier) }
    case 'resub': return { title: user + ' resubscribed!', detail: (d.months || 0) + ' months ' + tierText(d.tier) }
    case 'gift': return { title: user + ' gifted ' + (d.amount || 1) + ' sub' + (Number(d.amount) === 1 ? '' : 's') + '!', detail: tierText(d.tier) }
    case 'cheer': return { title: user + ' cheered ' + (d.amount || 0) + ' bits!', detail: '' }
    case 'raid': return { title: user + ' is raiding!', detail: (d.viewers || 0) + ' viewers incoming' }
    case 'supporter.tip': return { title: user + ' tipped ' + money(d), detail: d.message || '' }
    case 'supporter.membership': return { title: user + ' joined as a member!', detail: d.message || '' }
    case 'supporter.merch': return { title: user + ' bought merch!', detail: d.message || '' }
    case 'supporter.charity': return { title: user + ' donated ' + money(d) + ' to charity!', detail: d.message || '' }
    default: return null
  }
}

function handle(type: string, data: any): void {
  const d: any = data || {}
  if (!enabled(type) || !passesThreshold(type, d)) return
  const card: AlertCard | null = cardFor(type, d)
  if (!card) return
  queue.push(card)
  if (!current.value) showNext()
}

// One card at a time: enter (next frame → .show), hold for durationMs, exit, then the next card after the fade.
function showNext(): void {
  const next: AlertCard | undefined = queue.shift()
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

const handlers: Record<string, (d: any) => void> = {}

onMounted(() => {
  if (!nnz) return
  // onSettings fires immediately with saved settings and again on every change — react live.
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (Array.isArray(s.events)) cfg.events = s.events.slice()
    if (typeof s.textTemplate === 'string') cfg.textTemplate = s.textTemplate
    if (isFinite(Number(s.durationMs))) cfg.durationMs = Number(s.durationMs)
    if (isFinite(Number(s.minBits))) cfg.minBits = Number(s.minBits)
    if (isFinite(Number(s.minGiftCount))) cfg.minGiftCount = Number(s.minGiftCount)
    if (isFinite(Number(s.minAmount))) cfg.minAmount = Number(s.minAmount)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  ALL_EVENTS.forEach((type: string) => {
    const fn = (d: any) => handle(type, d)
    handlers[type] = fn
    nnz.on(type, fn)
  })
})

onUnmounted(() => {
  if (timer) window.clearTimeout(timer)
  if (!nnz) return
  Object.keys(handlers).forEach((type: string) => nnz.off(type, handlers[type]))
})
</script>

<template>
  <div class="nnz-alerts" :style="{ '--accent': cfg.accentColor }">
    <div v-if="current" :key="cardKey" class="card" :class="{ show: visible }">
      <div class="title">{{ current.title }}</div>
      <div v-if="current.detail" class="detail">{{ current.detail }}</div>
    </div>
  </div>
</template>

<style scoped>
.nnz-alerts {
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
.title {
  font-size: 27px;
  font-weight: 800;
  letter-spacing: 0.2px;
  color: var(--accent, #9146ff);
  text-shadow: 0 1px 12px color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
}
.detail {
  margin-top: 8px;
  font-size: 17px;
  font-weight: 500;
  opacity: 0.92;
}
</style>
