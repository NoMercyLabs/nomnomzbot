<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.
// Every picture and sound comes from the theme pack the pack_id setting names; this file only places them.

interface Flight {
  id: number
  player: string
  seed: string
  spawnX: number
  landingX: number
  score: number
  hit: boolean
  down: boolean
}
interface Winner { player: string; payout: number }
interface Row { key: string; rank: number; player: string; value: string; payout: boolean }

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']
type Pack = NnzDropPackManifest
type Box = NnzDropPackSize

const FLIGHT_MS = 5000

const cfg = reactive({ hideAfterMs: 12000, packId: '', boardAnchor: 'top' as string })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved' | 'cancelled'>('lobby')
const target = ref<number>(50)
const radius = ref<number>(10)
const flights = ref<Flight[]>([])
const winners = ref<Winner[]>([])
const pack = ref<Pack | null>(null)
const packUrl = ref<string>('')
const scale = ref<number>(1)
const root = ref<HTMLElement | null>(null)
let nextId = 0
let hideTimer: number | undefined
let packTicket = 0
let pending: NnzDropPack | null = null
let observer: ResizeObserver | undefined
const started = new Set<number>()

const best = computed<Flight[]>(() =>
  flights.value
    .filter((f) => f.down)
    .sort((a, b) => b.score - a.score)
    .slice(0, 5),
)

// The board only exists when it has rows: payouts once a round pays, else the best landings so far.
const boardRows = computed<Row[]>(() => {
  if (phase.value === 'resolved' && winners.value.length) {
    return winners.value.slice(0, 5).map((w, i): Row => ({ key: w.player, rank: i + 1, player: w.player, value: '+' + w.payout, payout: true }))
  }
  return best.value.map((f, i): Row => ({ key: String(f.id), rank: i + 1, player: f.player, value: String(f.score), payout: false }))
})

const boardTitle = computed<string>(() => {
  if (phase.value === 'cancelled') return 'Called off. Next time.'
  if (phase.value === 'resolved') return winners.value.length ? 'Nailed it.' : 'Nobody hit it.'
  return ''
})

const stage = computed<Box>(() => pack.value?.stage ?? { w: 1920, h: 1080 })
const slots = computed(() => pack.value?.slots ?? null)

// Where a payload stands: the top of the target band, from the band's own hitbox.
const groundY = computed<number>(() => {
  const t = slots.value?.target
  return t ? stage.value.h - t.size.h + t.hitbox.y : stage.value.h
})

const stageStyle = computed(() => ({
  width: stage.value.w + 'px',
  height: stage.value.h + 'px',
  transform: 'translate(-50%, -50%) scale(' + scale.value + ')',
}))

// The art text by URL. It goes into the page inline, so its --drop-* colors follow the theme.
const drawings = reactive<Record<string, string>>({})

function resolve(src: string | null | undefined, base: string): string {
  return src && base ? new URL(src, base).href : ''
}

function art(src: string | null | undefined): string {
  return resolve(src, packUrl.value)
}

function drawing(src: string | null | undefined): string {
  return drawings[art(src)] ?? ''
}

function sourcesOf(m: Pack): string[] {
  const s = m.slots
  const all = [s.background?.src, s.foreground?.src, s.carrier?.src, s.payload.src, s.target.marker?.src, s.landed.miss?.src]
  return [...all, ...s.landed.stages.map((x) => x.src), ...(m.effects ?? []).map((e) => e.src)].filter((x): x is string => !!x)
}

// A drawing fills its box, bottom-centred; scripts and event handlers never reach the page.
function fitted(text: string): string {
  const doc = new DOMParser().parseFromString(text, 'image/svg+xml')
  const el = doc.documentElement
  if (el.localName !== 'svg' || doc.querySelector('parsererror')) return ''
  doc.querySelectorAll('script, foreignObject').forEach((n) => n.remove())
  doc.querySelectorAll('*').forEach((n) => {
    for (const a of Array.from(n.attributes)) if (a.name.startsWith('on')) n.removeAttribute(a.name)
  })
  el.setAttribute('preserveAspectRatio', 'xMidYMax meet')
  el.setAttribute('width', '100%')
  el.setAttribute('height', '100%')
  return new XMLSerializer().serializeToString(el)
}

async function fetchDrawing(url: string): Promise<void> {
  if (url in drawings) return
  drawings[url] = fitted(await NomNomz.data.dropArt(url))
}

function preload(p: NnzDropPack): Promise<void[]> {
  return Promise.all(sourcesOf(p.manifest).map((src) => fetchDrawing(resolve(src, p.url))))
}

function boxStyle(size: Box, z: number | undefined): Record<string, string> {
  return { width: size.w + 'px', height: size.h + 'px', zIndex: String(z ?? 1) }
}

const bandStyle = computed(() => {
  const t = slots.value?.target
  if (!t) return {}
  const box = boxStyle({ w: stage.value.w, h: t.size.h }, t.z)
  if (!t.src) return box
  return {
    ...box,
    backgroundImage: 'url("' + art(t.src) + '")',
    backgroundSize: t.size.w + 'px ' + t.size.h + 'px',
    backgroundRepeat: t.repeat === 'x' ? 'repeat-x' : 'no-repeat',
  }
})

// The marker's hitbox spans the round's real scoring zone (target ± radius), so what you see is what scores.
// Its height follows its own aspect; its centre sits on the ground line, so plants stand on its top face.
const markerStyle = computed(() => {
  const t = slots.value?.target
  const m = t?.marker
  if (!t || !m) return null
  const zone = (radius.value * 2 * stage.value.w) / 100
  const w = (zone * m.size.w) / t.hitbox.w
  const h = (w * m.size.h) / m.size.w
  return {
    ...boxStyle({ w, h }, (t.z ?? 1) + 1),
    left: (target.value * stage.value.w) / 100 - w / 2 + 'px',
    top: groundY.value - h / 2 + 'px',
  }
})

function labelStyle(): Record<string, string> {
  const colors = pack.value?.labels?.colors
  const out: Record<string, string> = {}
  if (colors?.text?.startsWith('--nnz-')) out.color = 'var(' + colors.text + ')'
  if (colors?.background?.startsWith('--nnz-')) out.background = 'var(' + colors.background + ')'
  return out
}

function initial(player: string): string {
  return (Array.from(player.trim())[0] ?? '?').toUpperCase()
}

// A higher score grows a bigger plant: the scale runs from the pack's minScale to its maxScale.
function growthOf(f: Flight): number {
  const g = slots.value?.landed.growWithScore
  if (!g) return 1
  const t = Math.max(0, Math.min(100, f.score)) / 100
  return g.minScale + (g.maxScale - g.minScale) * t
}

function landedDrawing(f: Flight): string {
  const landed = slots.value?.landed
  if (!landed) return ''
  if (f.score <= 0) return drawing(landed.miss?.src)
  const stageFor = [...landed.stages].reverse().find((s) => f.score >= s.minScore)
  return drawing(stageFor?.src)
}

function landedStyle(f: Flight): Record<string, string> {
  const landed = slots.value?.landed
  if (!landed) return {}
  const k = f.score > 0 ? growthOf(f) : 1
  const w = landed.size.w * k
  const h = landed.size.h * k
  return {
    ...boxStyle({ w, h }, landed.z),
    left: (f.landingX * stage.value.w) / 100 - w / 2 + 'px',
    top: groundY.value - h + 'px',
  }
}

function effectFor(f: Flight) {
  if (!pack.value || !f.down) return null
  const on = f.hit ? 'hit' : f.score > 0 ? 'land' : 'miss'
  return pack.value.effects?.find((e) => e.on === on) ?? null
}

function effectStyle(f: Flight, size: Box): Record<string, string> {
  return {
    ...boxStyle(size, 9),
    left: (f.landingX * stage.value.w) / 100 - size.w / 2 + 'px',
    top: groundY.value - size.h / 2 + 'px',
  }
}

function play(name: 'spawn' | 'land' | 'hit' | 'miss'): void {
  const src = pack.value?.sounds?.[name]
  if (!src) return
  const audio = new Audio(art(src))
  audio.play().catch(() => {})
}

// A small seeded generator, so every overlay that gets the same seed draws the same sway.
function seeded(seed: string, player: string): () => number {
  let state = 0
  try {
    state = Number(BigInt.asUintN(32, BigInt(seed)))
  } catch {
    for (const ch of player) state = (state * 31 + ch.charCodeAt(0)) >>> 0
  }
  return () => {
    state = (state + 0x6d2b79f5) >>> 0
    let t = state
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

// The server decides the result. The path starts at spawnX above the stage, sways with the seed (cosmetic only),
// and ends exactly on the server's landingX, standing on the ground: the sway fades to zero at the last frame.
function fly(el: Element | null, f: Flight): void {
  if (!(el instanceof HTMLElement) || started.has(f.id) || !slots.value) return
  started.add(f.id)
  play('spawn')
  if (typeof el.animate !== 'function') { land(f); return }
  const payload = slots.value.payload.size
  const carrier = slots.value.carrier?.size ?? { w: 0, h: 0 }
  const rand = seeded(f.seed, f.player)
  const sway = pack.value?.motion.sway ?? 0
  const amplitude = (3 + rand() * 6) * (stage.value.w / 100)
  const turns = 1.5 + rand() * 2
  const offset = rand() * Math.PI * 2
  const fromY = -(payload.h + carrier.h)
  const toY = groundY.value - payload.h
  const steps = 24
  const frames: Keyframe[] = []
  for (let i = 0; i <= steps; i++) {
    const t = i / steps
    const wave = Math.sin(offset + t * turns * Math.PI * 2)
    const x = ((f.spawnX + (f.landingX - f.spawnX) * t) * stage.value.w) / 100 + amplitude * wave * (1 - t)
    const y = fromY + (toY - fromY) * t
    frames.push({ transform: 'translate(' + (x - payload.w / 2) + 'px, ' + y + 'px) rotate(' + wave * sway * (1 - t) + 'deg)' })
  }
  const animation = el.animate(frames, { duration: FLIGHT_MS, easing: 'linear', fill: 'forwards' })
  animation.onfinish = () => land(f)
}

function land(f: Flight): void {
  const found = flights.value.find((x) => x.id === f.id)
  if (!found) return
  found.down = true
  play(found.hit ? 'hit' : found.score > 0 ? 'land' : 'miss')
  applyPending()
}

function reset(): void {
  flights.value = []
  winners.value = []
  started.clear()
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
  applyPending()
}

// A new pack swaps in at the next idle moment, so a drop in flight finishes in the pack it started in.
function applyPending(): void {
  if (!pending || flights.value.some((f) => !f.down)) return
  pack.value = pending.manifest
  packUrl.value = pending.url
  pending = null
  rescale()
}

async function loadPack(id: string): Promise<void> {
  const ticket = ++packTicket
  try {
    const loaded = await NomNomz.data.dropPack(id)
    await preload(loaded)
    if (ticket !== packTicket) return
    pending = loaded
    applyPending()
  } catch {
    NomNomz.reportError('drop game: the theme pack "' + id + '" did not load, the current one stays')
  }
}

function rescale(): void {
  const el = root.value
  if (!el) return
  scale.value = Math.min(el.clientWidth / stage.value.w, el.clientHeight / stage.value.h)
}

// Frames arrive typed by phase; the payload 'kind' separates a round opening from an individual drop.
function onFrame(d: GameFrame): void {
  if (d.kind === 'round_open') {
    reset()
    visible.value = true
    phase.value = 'lobby'
    target.value = d.target ?? 50
    radius.value = d.radius || 10
    return
  }
  if (d.kind === 'drop') {
    visible.value = true
    flights.value = [...flights.value, {
      id: nextId++,
      player: d.player,
      seed: d.seed ?? '',
      spawnX: d.spawnX,
      landingX: d.landingX,
      score: d.score ?? (d.hit ? 50 : 0),
      hit: d.hit,
      down: false,
    }]
    return
  }
  if (d.kind === 'cancelled') {
    phase.value = 'cancelled'
    scheduleHide()
    return
  }
  if (d.kind === 'results') {
    phase.value = 'resolved'
    if (d.target != null) target.value = d.target
    winners.value = d.results
      .filter((r) => r.won === true)
      .sort((a, b) => b.payout - a.payout)
      .map((r): Winner => ({ player: r.player, payout: r.payout }))
    scheduleHide()
  }
}

function scheduleHide(): void {
  if (hideTimer) clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => { visible.value = false }, cfg.hideAfterMs)
}

onMounted(() => {
  if (typeof ResizeObserver === 'function' && root.value) {
    observer = new ResizeObserver(rescale)
    observer.observe(root.value)
  }
  rescale()
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (isFinite(Number(s.hideAfterMs)) && Number(s.hideAfterMs) > 0) cfg.hideAfterMs = Number(s.hideAfterMs)
    if (typeof s.boardAnchor === 'string' && s.boardAnchor) cfg.boardAnchor = s.boardAnchor
    const id = typeof s.pack_id === 'string' ? s.pack_id : ''
    if (!pack.value || id !== cfg.packId) {
      cfg.packId = id
      void loadPack(id)
    }
  })
  NomNomz.on('game.lobby', onFrame)
  NomNomz.on('game.running', onFrame)
  NomNomz.on('game.resolved', onFrame)
})

onUnmounted(() => {
  observer?.disconnect()
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('game.lobby', onFrame)
  NomNomz.off('game.running', onFrame)
  NomNomz.off('game.resolved', onFrame)
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens) and the theme pack's art. -->
  <div ref="root" class="nnz-widget nnz-drop">
    <Transition name="nnz">
      <div v-if="visible && pack && slots" class="scene">
        <div v-if="flights.length" class="stage" :style="stageStyle">
          <span v-if="slots.background" class="layer art" :style="boxStyle(stage, slots.background.z)" v-html="drawing(slots.background.src)"></span>
          <div class="band" :style="bandStyle"></div>
          <span v-if="markerStyle && slots.target.marker" class="marker art" :style="markerStyle" v-html="drawing(slots.target.marker.src)"></span>

          <template v-for="f in flights" :key="f.id">
            <span v-if="f.down" class="landed art" :style="landedStyle(f)" v-html="landedDrawing(f)"></span>
            <span v-if="effectFor(f)" class="effect art" :style="effectStyle(f, effectFor(f)!.size)" v-html="drawing(effectFor(f)?.src)"></span>
          </template>

          <div
            v-for="f in flights"
            :key="'drop' + f.id"
            :ref="(el) => fly(el as Element | null, f)"
            class="drop"
            :class="{ 'is-down': f.down, 'is-miss': f.down && f.score <= 0 }"
            :style="{ width: slots.payload.size.w + 'px', height: slots.payload.size.h + 'px', zIndex: String(slots.payload.z ?? 3) }"
          >
            <span v-if="slots.carrier" class="carrier art" :style="boxStyle(slots.carrier.size, slots.carrier.z)" v-html="drawing(slots.carrier.src)"></span>
            <span class="tile nnz-title">{{ initial(f.player) }}</span>
            <span class="frame art" v-html="drawing(slots.payload.src)"></span>
            <span v-if="f.down && f.score > 0" class="score nnz-chip">{{ f.score }}</span>
            <span class="nnz-pill name" :style="labelStyle()">{{ f.player }}</span>
          </div>

          <span v-if="slots.foreground" class="layer art" :style="boxStyle(stage, slots.foreground.z)" v-html="drawing(slots.foreground.src)"></span>
        </div>

        <div v-if="boardRows.length" class="nnz-card nnz-placed board" :data-anchor="cfg.boardAnchor">
          <div class="nnz-chip">Drop Game</div>
          <div v-if="boardTitle" class="nnz-title title">{{ boardTitle }}</div>
          <div v-for="(r, i) in boardRows" :key="r.key" class="nnz-row" :class="{ 'is-first': i === 0 }">
            <span v-if="!r.payout" class="nnz-num">{{ r.rank }}</span>
            <span class="nnz-grow">{{ r.player }}</span>
            <span class="nnz-end" :class="{ 'nnz-accent-text': r.payout }">{{ r.value }}</span>
          </div>
        </div>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
.nnz-drop {
  position: fixed;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  /* The pack's green parts take the channel's own color in every theme. */
  --drop-own: var(--nnz-accent);
  --drop-canopy: var(--drop-own);
  --drop-shade: color-mix(in srgb, var(--drop-own) 45%, #121212); /* theme-exempt: the canopy art's own shadow tone */
  --drop-shade-dark: color-mix(in srgb, var(--drop-own) 25%, #121212); /* theme-exempt: the canopy art's own shadow tone */
}
/* Print puts the channel's color on the surface; its accent is the ink. */
[data-nnz-theme='print'] .nnz-drop {
  --drop-own: var(--nnz-surface);
}
/* Classic keeps the pack's own outline and gold; the other themes draw them in their line and accent. */
[data-nnz-theme='sticker'] .nnz-drop,
[data-nnz-theme='ink'] .nnz-drop,
[data-nnz-theme='print'] .nnz-drop {
  --drop-canopy-alt: var(--nnz-line);
  --drop-outline: var(--nnz-line);
  --drop-gold: var(--nnz-accent);
  --drop-sparkle-dot: var(--nnz-surface);
}
.scene {
  position: absolute;
  inset: 0;
}
.stage {
  position: absolute;
  left: 50%;
  top: 50%;
  transform-origin: 50% 50%;
}
.stage > * {
  position: absolute;
}
.layer {
  left: 0;
  top: 0;
}
.band {
  left: 0;
  bottom: 0;
}
.art {
  display: block;
}
.art :deep(svg) {
  display: block;
  width: 100%;
  height: 100%;
  overflow: visible;
}
.landed {
  transform-origin: 50% 100%;
  animation: grow 900ms var(--nnz-ease) both;
}
.effect {
  animation: burst 900ms var(--nnz-ease) both;
}
.drop {
  left: 0;
  top: 0;
  will-change: transform;
}
.carrier {
  position: absolute;
  left: 50%;
  bottom: 88%;
  translate: -50% 0;
  transform-origin: 50% 100%;
  transition: opacity var(--nnz-out) ease-in, scale var(--nnz-out) ease-in;
}
.is-down .carrier {
  opacity: 0;
  scale: 1 0.1;
}
.tile {
  position: absolute;
  inset: 12%;
  display: grid;
  place-items: center;
  border-radius: 999px;
  background: var(--nnz-surface);
  color: var(--nnz-text);
  font-size: 40px;
  line-height: 1;
  margin: 0;
}
.frame {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
}
.name {
  position: absolute;
  left: 50%;
  top: calc(100% + 10px);
  translate: -50% 0;
  padding: 4px 14px;
  font-size: 22px;
  font-weight: 800;
  white-space: nowrap;
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  box-shadow: none;
  zoom: 1;
}
/* Once down, the plant takes the spot; the name stays in the grass under it. */
.is-down .tile,
.is-down .frame {
  animation: settle 700ms ease-in 500ms both;
}
.score {
  position: absolute;
  left: 50%;
  bottom: calc(100% + 12px);
  translate: -50% 0;
  font-size: 26px;
  animation: pop 1800ms var(--nnz-ease) both;
}
.is-miss .tile,
.is-miss .frame,
.is-miss .name {
  opacity: 0.6;
  filter: grayscale(1);
}
.board {
  width: 300px;
}
.title {
  margin-bottom: 0.4em;
}
@keyframes grow {
  0% { scale: 0; }
  100% { scale: 1; }
}
@keyframes settle {
  0% { opacity: 1; scale: 1; }
  100% { opacity: 0; scale: 0.4; }
}
@keyframes burst {
  0% { opacity: 0; scale: 0.3; }
  30% { opacity: 1; scale: 1.1; }
  100% { opacity: 0; scale: 1.3; }
}
@keyframes pop {
  0% { opacity: 0; scale: 0.6; }
  20% { opacity: 1; scale: 1.15; }
  80% { opacity: 1; scale: 1; }
  100% { opacity: 0; scale: 1; }
}
</style>
