<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, onMounted, onUnmounted } from 'vue'

// The overlay SDK global (window.NomNomz), injected before this bundle runs. Loose type by design.
const nnz = (window as any).NomNomz

const ALL_EVENTS: string[] = [
  'follow', 'subscription', 'resub', 'gift', 'cheer', 'raid',
  'supporter.tip', 'supporter.membership', 'supporter.merch', 'supporter.charity',
]

interface TickerConfig { events: string[]; speed: number; count: number; accentColor: string }
const cfg = reactive<TickerConfig>({ events: ALL_EVENTS.slice(), speed: 60, count: 20, accentColor: '#9146ff' })

interface Chip { id: number; icon: string; text: string }
const chips = ref<Chip[]>([])
let seq = 0

// Two copies of the chip row scroll left in lockstep; when the first copy fully passes, the offset wraps by its
// exact width for a seamless loop. Widths are read from the live DOM each frame so trims/appends never desync it.
const trackEl = ref<HTMLElement | null>(null)
const copyEl = ref<HTMLElement | null>(null)
let offset = 0
let raf = 0
let last = 0

function money(d: any): string {
  const amount: number = Number(d.amount) || 0
  return d.currency ? amount + ' ' + d.currency : String(amount)
}

function chipFor(type: string, data: any): Chip | null {
  const d: any = data || {}
  const user: string = d.user || 'Someone'
  switch (type) {
    case 'follow': return { id: ++seq, icon: '★', text: user + ' followed' }
    case 'subscription': return { id: ++seq, icon: '✦', text: user + ' subscribed' }
    case 'resub': return { id: ++seq, icon: '✦', text: user + ' resubbed ' + (d.months || 0) + 'mo' }
    case 'gift': return { id: ++seq, icon: '✚', text: user + ' gifted ' + (d.amount || 1) }
    case 'cheer': return { id: ++seq, icon: '◆', text: user + ' cheered ' + (d.amount || 0) }
    case 'raid': return { id: ++seq, icon: '⚑', text: user + ' raided ' + (d.viewers || 0) }
    case 'supporter.tip': return { id: ++seq, icon: '♥', text: user + ' tipped ' + money(d) }
    case 'supporter.membership': return { id: ++seq, icon: '♥', text: user + ' membership' }
    case 'supporter.merch': return { id: ++seq, icon: '♥', text: user + ' merch' }
    case 'supporter.charity': return { id: ++seq, icon: '♥', text: user + ' charity ' + money(d) }
    default: return null
  }
}

function handle(type: string, data: any): void {
  if (cfg.events.indexOf(type) === -1) return
  const chip: Chip | null = chipFor(type, data)
  if (!chip) return
  const next: Chip[] = chips.value.concat([chip])
  while (next.length > Math.max(1, cfg.count)) next.shift() // retire the oldest past the retained count
  chips.value = next
}

function frame(t: number): void {
  if (!last) last = t
  const dt: number = t - last
  last = t
  const track: HTMLElement | null = trackEl.value
  const copy: HTMLElement | null = copyEl.value
  if (track && copy && chips.value.length) {
    offset -= (cfg.speed * dt) / 1000
    const w: number = copy.offsetWidth
    if (w > 0 && -offset >= w) offset += w
    track.style.transform = 'translateX(' + offset + 'px)'
  } else {
    offset = 0
  }
  raf = requestAnimationFrame(frame)
}

const handlers: Record<string, (d: any) => void> = {}

onMounted(() => {
  raf = requestAnimationFrame(frame)
  if (!nnz) return
  nnz.onSettings((s: any) => {
    if (!s || typeof s !== 'object') return
    if (Array.isArray(s.events)) cfg.events = s.events.slice()
    if (isFinite(Number(s.speed)) && Number(s.speed) > 0) cfg.speed = Number(s.speed)
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  ALL_EVENTS.forEach((type: string) => {
    const fn = (d: any) => handle(type, d)
    handlers[type] = fn
    nnz.on(type, fn)
  })
})

onUnmounted(() => {
  if (raf) cancelAnimationFrame(raf)
  if (!nnz) return
  Object.keys(handlers).forEach((type: string) => nnz.off(type, handlers[type]))
})
</script>

<template>
  <div class="nnz-ticker" :style="{ '--accent': cfg.accentColor }">
    <div ref="trackEl" class="track">
      <div ref="copyEl" class="copy">
        <span v-for="c in chips" :key="c.id" class="chip">
          <span class="icon">{{ c.icon }}</span><span class="text">{{ c.text }}</span>
        </span>
      </div>
      <div class="copy" aria-hidden="true">
        <span v-for="c in chips" :key="'d' + c.id" class="chip">
          <span class="icon">{{ c.icon }}</span><span class="text">{{ c.text }}</span>
        </span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.nnz-ticker {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  height: 44px;
  display: flex;
  align-items: center;
  overflow: hidden;
  background: linear-gradient(180deg, rgba(12, 12, 18, 0) 0%, rgba(12, 12, 18, 0.85) 100%);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
.track {
  display: flex;
  flex: 0 0 auto;
  white-space: nowrap;
  will-change: transform;
}
.copy {
  display: flex;
  flex: 0 0 auto;
}
.chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex: 0 0 auto;
  margin-right: 12px;
  padding: 6px 14px;
  border-radius: 999px;
  color: #fff;
  background: rgba(255, 255, 255, 0.06);
  border: 1px solid color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
  font-size: 15px;
  font-weight: 600;
}
.icon {
  color: var(--accent, #9146ff);
  font-size: 14px;
}
.text {
  opacity: 0.95;
}
</style>
