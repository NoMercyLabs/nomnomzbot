<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, shallowRef, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) comes from this widget's own SDK types. An event payload is read as a loose record: one
// handler serves several event types, so each field is narrowed where it is read.
type Payload = Record<string, unknown>

function asPayload(data: unknown): Payload {
  return data !== null && typeof data === 'object' ? (data as Payload) : {}
}

const ALL_EVENTS: string[] = [
  'follow', 'subscription', 'resub', 'gift', 'cheer', 'raid',
  'supporter.tip', 'supporter.membership', 'supporter.merch', 'supporter.charity',
]

interface TickerConfig {
  events: string[]; speed: number; count: number; accentColor: string; anchor: string; fullWidth: boolean; width: number
}
const cfg = reactive<TickerConfig>({
  events: ALL_EVENTS.slice(),
  speed: 60,
  count: 20,
  accentColor: '#9146ff',
  anchor: 'center',
  fullWidth: true,
  width: 800,
})

// Each side's distance to the source edge: its own margin (set by the overlay runtime), else the edge offset.
const MARGIN = {
  top: 'var(--nnz-m-top, var(--nnz-edge))',
  right: 'var(--nnz-m-right, var(--nnz-edge))',
  bottom: 'var(--nnz-m-bottom, var(--nnz-edge))',
  left: 'var(--nnz-m-left, var(--nnz-edge))',
}

// The size hook: full width spans the whole source; otherwise the band is `width` px wide (never wider than the
// room the side margins leave) and the anchor's horizontal side places it, with that side's margin as the gap.
function useBandSize(c: TickerConfig) {
  return computed<Record<string, string>>(() => {
    const parts: string[] = c.anchor.split('-')
    const vertical: string = parts[0] === 'top' || parts[0] === 'bottom' ? parts[0] : 'center'
    const horizontal: string = parts.includes('left') ? 'left' : parts.includes('right') ? 'right' : 'center'
    const style: Record<string, string> = {
      top: vertical === 'top' ? MARGIN.top : vertical === 'bottom' ? 'auto' : '50%',
      bottom: vertical === 'bottom' ? MARGIN.bottom : 'auto',
      '--ty': vertical === 'center' ? 'calc(-50% + (' + MARGIN.top + ' - ' + MARGIN.bottom + ') / 2)' : '0',
    }
    if (c.fullWidth) return { ...style, left: '0', right: '0', '--tx': '0' }
    const width: string = 'min(' + c.width + 'px, calc(100% - ' + MARGIN.left + ' - ' + MARGIN.right + '))'
    if (horizontal === 'left') return { ...style, width, left: MARGIN.left, '--tx': '0' }
    if (horizontal === 'right') return { ...style, width, right: MARGIN.right, '--tx': '0' }
    return { ...style, width, left: '50%', '--tx': 'calc(-50% + (' + MARGIN.left + ' - ' + MARGIN.right + ') / 2)' }
  })
}
const bandStyle = useBandSize(cfg)

const TEST_SHOW_MS = 10000
const GAP = 18 // the least room between two chips

interface Chip { id: number; icon: string; text: string }

// The conveyor: every chip is on the belt exactly once. A chip that leaves on the left is put back behind the last
// chip, so the row always scrolls, loops without a hole and never shows a chip twice. Positions live outside Vue
// state, so a frame never re-renders. `fresh`: a new chip that has not come on screen yet.
interface Slot { key: number; chip: Chip; x: number; w: number; once: boolean; fresh: boolean; el: HTMLElement | null }
const slots = shallowRef<Slot[]>([]) // what is rendered (order does not matter)
let belt: Slot[] = [] // left to right, the order the conveyor runs in
let spacing = GAP // the room between two chips now; wider on a short loop, so the loop has no seam
let keySeq = 0
let seq = 0
let raf = 0
let last = 0

function setEl(slot: Slot, el: unknown): void {
  slot.el = el instanceof HTMLElement ? el : null
}

function rightOf(s: Slot): number {
  return s.x + s.w
}

function isOffscreen(s: Slot): boolean {
  return s.x >= bandWidth.value
}

// A new chip joins behind the other new chips that wait off screen, so a burst shows in the order it came. It goes
// before the chips that come round again, so it shows up next, not after a full loop.
function addSlot(chip: Chip, once: boolean): Slot {
  const slot: Slot = { key: ++keySeq, chip, x: bandWidth.value, w: 0, once, fresh: true, el: null }
  let at: number = belt.findIndex((s: Slot) => isOffscreen(s) && !s.fresh)
  if (at === -1) at = belt.length
  const before: Slot | undefined = belt[at - 1]
  slot.x = Math.max(bandWidth.value, before ? rightOf(before) + spacing : 0)
  belt.splice(at, 0, slot)
  slots.value = slots.value.concat([slot])
  return slot
}

function dropSlot(slot: Slot): void {
  belt = belt.filter((s: Slot) => s !== slot)
  slots.value = slots.value.filter((s: Slot) => s !== slot)
}

// Off screen: gone at once. On screen: it rides out and is not put back.
function retire(slot: Slot): void {
  if (isOffscreen(slot)) return dropSlot(slot)
  slot.once = true
}

// Supporter events carry the amount in minor units (SupporterAlertPayload.amountMinor, cents).
function money(d: Payload): string {
  const amount: number = (Number(d.amountMinor) || 0) / 100
  return d.currency ? amount.toFixed(2) + ' ' + String(d.currency) : amount.toFixed(2)
}

// The wire DTOs (AlertDtos.cs, camelCase) name their subject field differently per event type: FollowAlertDto/
// SubscriptionAlertDto/ResubAlertDto/CheerAlertDto use `displayName`; GiftSubAlertDto uses `gifterDisplayName`
// (no subscriber identity, only the gifter); RaidAlertDto uses `fromDisplayName`. The real supporter.* payload
// (SupporterWidgetEventHandler: SupporterAlertPayload) uses `supporterDisplayName`.
function nameOf(type: string, d: Payload): string {
  if (type === 'gift') return String(d.gifterDisplayName || 'Someone')
  if (type === 'raid') return String(d.fromDisplayName || 'Someone')
  if (type.indexOf('supporter.') === 0) return String(d.supporterDisplayName || 'Someone')
  return String(d.displayName || d.user || 'Someone')
}

function chipFor(type: string, data: unknown): Chip | null {
  const d: Payload = asPayload(data)
  const user: string = nameOf(type, d)
  switch (type) {
    case 'follow': return { id: ++seq, icon: '★', text: user + ' followed' }
    case 'subscription': return { id: ++seq, icon: '✦', text: user + ' subscribed' }
    case 'resub': return { id: ++seq, icon: '✦', text: user + ' resubbed ' + String(d.months || 0) + 'mo' }
    case 'gift': return { id: ++seq, icon: '✚', text: user + ' gifted ' + String(d.count || 1) }
    case 'cheer': return { id: ++seq, icon: '◆', text: user + ' cheered ' + String(d.bits || 0) }
    case 'raid': return { id: ++seq, icon: '⚑', text: user + ' raided ' + String(d.viewerCount || 0) }
    case 'supporter.tip': return { id: ++seq, icon: '♥', text: user + ' tipped ' + money(d) }
    case 'supporter.membership': return { id: ++seq, icon: '♥', text: user + ' became a member' + (d.quantity ? ' (' + String(d.quantity) + 'mo)' : '') }
    case 'supporter.merch': return { id: ++seq, icon: '♥', text: user + ' bought merch' + (d.quantity ? ' x' + String(d.quantity) : '') }
    case 'supporter.charity': return { id: ++seq, icon: '♥', text: user + ' donated ' + money(d) + ' to charity' }
    default: return null
  }
}

function handle(type: string, data: unknown, meta?: NnzEventMeta): void {
  if (cfg.events.indexOf(type) === -1) return
  const chip: Chip | null = chipFor(type, data)
  if (!chip) return
  if (meta && meta.test) {
    // A dashboard test fire is shown for a short time and never stays in the real ticker.
    const slot: Slot = addSlot(chip, true)
    window.setTimeout(() => { if (belt.includes(slot)) retire(slot) }, TEST_SHOW_MS)
    return
  }
  addSlot(chip, false)
  // Retire the oldest real chips past the retained count.
  const kept: Slot[] = belt.filter((s: Slot) => !s.once).sort((a: Slot, b: Slot) => a.chip.id - b.chip.id)
  kept.slice(0, Math.max(0, kept.length - Math.max(1, cfg.count))).forEach(retire)
}

const bandEl = ref<HTMLElement | null>(null)
const bandWidth = ref(0)
let resize: ResizeObserver | null = null
function measure(): void {
  belt.forEach((s: Slot) => { if (s.el) s.w = s.el.offsetWidth })
}

// The room between two chips. A loop must be at least the band plus its widest chip, or the chip that comes
// round leaves a hole at the right. A short row always scrolls too: its spare room goes evenly into every gap.
function loopSpacing(): number {
  const kept: Slot[] = belt.filter((s: Slot) => !s.once)
  if (kept.length === 0) return GAP
  const sum: number = kept.reduce((t: number, s: Slot) => t + s.w, 0)
  const widest: number = kept.reduce((m: number, s: Slot) => Math.max(m, s.w), 0)
  const loop: number = Math.max(sum + kept.length * GAP, bandWidth.value + widest + GAP)
  return (loop - sum) / kept.length
}

// Move every chip left. A chip off screen closes up behind the chip in front. A chip on screen never jumps: when
// it is too close, it waits until there is room.
function advance(step: number): void {
  const band: number = bandWidth.value
  belt.forEach((s: Slot, i: number) => {
    const from: number = s.x
    s.x -= step
    const prev: Slot | undefined = belt[i - 1]
    if (prev) {
      const behind: number = rightOf(prev) + spacing
      if (s.x > band) s.x = Math.max(behind, band)
      else if (s.x < behind) s.x = Math.min(behind, from)
    }
    if (s.x < band) s.fresh = false
  })
}

// A chip that has fully left on the left goes back behind the last chip, unless it was a one-off.
function recycle(): void {
  const head: Slot | undefined = belt[0]
  if (!head || rightOf(head) >= 0) return
  belt.shift()
  if (head.once) {
    slots.value = slots.value.filter((s: Slot) => s !== head)
    return
  }
  const tail: Slot | undefined = belt[belt.length - 1]
  head.x = Math.max(bandWidth.value, tail ? rightOf(tail) + spacing : 0)
  belt.push(head)
}

function frame(t: number): void {
  if (!last) last = t
  const dt: number = Math.min(t - last, 100)
  last = t
  measure()
  spacing = loopSpacing()
  advance((cfg.speed * dt) / 1000)
  recycle()
  belt.forEach((s: Slot) => { if (s.el) s.el.style.transform = 'translateX(' + s.x + 'px)' })
  raf = requestAnimationFrame(frame)
}

const handlers: Record<string, (d: unknown, t?: string, meta?: NnzEventMeta) => void> = {}

onMounted(() => {
  if (bandEl.value) {
    bandWidth.value = bandEl.value.clientWidth
    if (typeof ResizeObserver !== 'undefined') {
      resize = new ResizeObserver(() => { if (bandEl.value) bandWidth.value = bandEl.value.clientWidth })
      resize.observe(bandEl.value)
    }
  }
  raf = requestAnimationFrame(frame)
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (Array.isArray(s.events)) cfg.events = s.events.slice()
    if (isFinite(Number(s.speed)) && Number(s.speed) > 0) cfg.speed = Number(s.speed)
    if (isFinite(Number(s.count)) && Number(s.count) > 0) cfg.count = Number(s.count)
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (typeof s.anchor === 'string' && s.anchor) cfg.anchor = s.anchor
    if (typeof s.fullWidth === 'boolean') cfg.fullWidth = s.fullWidth
    if (isFinite(Number(s.width)) && Number(s.width) > 0) cfg.width = Number(s.width)
  })
  ALL_EVENTS.forEach((type: string) => {
    const fn = (d: unknown, _t?: string, meta?: NnzEventMeta): void => handle(type, d, meta)
    handlers[type] = fn
    NomNomz.on(type, fn)
  })
})

onUnmounted(() => {
  if (raf) cancelAnimationFrame(raf)
  if (resize) resize.disconnect()
  if (typeof NomNomz === 'undefined') return
  Object.entries(handlers).forEach(([type, fn]) => NomNomz.off(type, fn))
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file only places the bar. -->
  <div ref="bandEl" class="nnz-widget nnz-ticker" :style="bandStyle">
    <span
      v-for="s in slots"
      :key="s.key"
      :ref="(el) => setEl(s, el)"
      class="nnz-pill chip"
      :style="{ transform: 'translateX(' + s.x + 'px)' }"
    >
      <span class="icon nnz-accent-text">{{ s.chip.icon }}</span><span class="text">{{ s.chip.text }}</span>
    </span>
  </div>
</template>

<style scoped>
/* All chips share one grid cell, so the band is exactly one chip tall; each chip is placed by its own transform. */
.nnz-ticker {
  position: absolute;
  display: grid;
  overflow: hidden;
  transform: translate(var(--tx), var(--ty));
}
.chip {
  grid-area: 1 / 1;
  justify-self: start;
  display: inline-flex;
  align-items: center;
  gap: 8px;
  white-space: nowrap;
  padding-top: 6px;
  padding-bottom: 6px;
  font-size: 15px;
  font-weight: 700;
  will-change: transform;
}
.icon {
  font-size: 14px;
}
</style>
