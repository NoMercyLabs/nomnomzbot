<!-- SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

interface Flight {
  id: number
  player: string
  seed: string
  spawnX: number
  landed: number
  score: number
  hit: boolean
  down: boolean
}
interface Winner { player: string; payout: number }

type GameFrame =
  | NnzWidgetEventMap['game.lobby']
  | NnzWidgetEventMap['game.running']
  | NnzWidgetEventMap['game.resolved']

const FLIGHT_MS = 5000

const cfg = reactive<NnzWidgetSettings>({ accentColor: '#9146ff', hideAfterMs: 12000 })

const visible = ref<boolean>(false)
const phase = ref<'lobby' | 'resolved' | 'cancelled'>('lobby')
const target = ref<number>(50)
const radius = ref<number>(10)
const flights = ref<Flight[]>([])
const winners = ref<Winner[]>([])
let nextId = 0
let hideTimer: number | undefined
const started = new Set<number>()

const platformStyle = computed(() => {
  const left = Math.max(0, target.value - radius.value)
  const right = Math.min(100, target.value + radius.value)
  return { left: left + '%', width: right - left + '%' }
})

const best = computed<Flight[]>(() =>
  flights.value
    .filter((f) => f.down)
    .sort((a, b) => b.score - a.score)
    .slice(0, 5),
)

function sizeOf(f: Flight): string {
  if (!f.down) return 'is-flying'
  if (f.score >= 67) return 'is-large'
  if (f.score >= 34) return 'is-medium'
  if (f.score > 0) return 'is-small'
  return 'is-miss'
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

// The path starts at spawnX, sways with the seed, and ends exactly on the server's landed value.
function fly(el: Element | null, f: Flight): void {
  if (!(el instanceof HTMLElement) || started.has(f.id)) return
  started.add(f.id)
  if (typeof el.animate !== 'function') { land(f); return }
  const rand = seeded(f.seed, f.player)
  const amplitude = 3 + rand() * 6
  const turns = 1.5 + rand() * 2
  const offset = rand() * Math.PI * 2
  const steps = 24
  const frames: Keyframe[] = []
  for (let i = 0; i <= steps; i++) {
    const t = i / steps
    const sway = Math.sin(offset + t * turns * Math.PI * 2)
    const x = f.spawnX + (f.landed - f.spawnX) * t + amplitude * sway * (1 - t)
    frames.push({
      left: Math.min(100, Math.max(0, x)) + '%',
      translate: '0 ' + (-110 * (1 - t)) + 'vh',
      rotate: sway * 14 * (1 - t) + 'deg',
    })
  }
  const animation = el.animate(frames, { duration: FLIGHT_MS, easing: 'linear', fill: 'forwards' })
  animation.onfinish = () => land(f)
}

function land(f: Flight): void {
  const found = flights.value.find((x) => x.id === f.id)
  if (found) found.down = true
}

function reset(): void {
  flights.value = []
  winners.value = []
  started.clear()
  if (hideTimer) { clearTimeout(hideTimer); hideTimer = undefined }
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
      spawnX: d.spawnX ?? d.landed,
      landed: d.landed,
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
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
    if (isFinite(Number(s.hideAfterMs)) && Number(s.hideAfterMs) > 0) cfg.hideAfterMs = Number(s.hideAfterMs)
  })
  NomNomz.on('game.lobby', onFrame)
  NomNomz.on('game.running', onFrame)
  NomNomz.on('game.resolved', onFrame)
})

onUnmounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('game.lobby', onFrame)
  NomNomz.off('game.running', onFrame)
  NomNomz.off('game.resolved', onFrame)
  if (hideTimer) clearTimeout(hideTimer)
})
</script>

<template>
  <!-- The look comes from /overlay/theme.css (.nnz-* parts, --nnz-* tokens); this file draws the stage. -->
  <Transition name="nnz">
  <div v-if="visible" class="nnz-widget nnz-drop">
    <div class="nnz-card board">
      <div class="nnz-chip">Drop Game</div>
      <template v-if="phase === 'cancelled'">
        <div class="nnz-title title">Called off. Next time.</div>
      </template>
      <template v-else-if="phase === 'resolved'">
        <div class="nnz-title title">{{ winners.length ? 'Nailed it.' : 'Nobody hit it.' }}</div>
        <div v-for="(w, i) in winners.slice(0, 5)" :key="w.player" class="nnz-row" :class="{ 'is-first': i === 0 }">
          <span class="nnz-grow">{{ w.player }}</span>
          <span class="nnz-end nnz-accent-text">+{{ w.payout }}</span>
        </div>
      </template>
      <template v-else>
        <div class="nnz-title title">Type !drop to play</div>
        <div v-for="(f, i) in best" :key="f.id" class="nnz-row" :class="{ 'is-first': i === 0 }">
          <span class="nnz-num">{{ i + 1 }}</span>
          <span class="nnz-grow">{{ f.player }}</span>
          <span class="nnz-end">{{ f.score }}</span>
        </div>
      </template>
    </div>

    <div class="ground">
      <div class="platform" :style="platformStyle"></div>
      <div class="center" :style="{ left: target + '%' }"></div>
    </div>

    <div
      v-for="f in flights"
      :key="f.id"
      :ref="(el) => fly(el as Element | null, f)"
      class="drop"
      :class="sizeOf(f)"
      :style="{ left: f.landed + '%' }"
    >
      <svg class="chute" viewBox="0 0 100 60" aria-hidden="true">
        <path class="canopy" d="M4 34C4 14 26 2 50 2s46 12 46 32c-8-5-15-5-23 0-8-5-15-5-23 0-8-5-15-5-23 0-8-5-15-5-23 0Z" />
        <path class="lines" d="M6 34L44 58M50 34V58M94 34L56 58" />
      </svg>
      <span class="score nnz-title">{{ f.score }}</span>
      <span class="nnz-face payload"></span>
      <span class="nnz-pill name">{{ f.player }}</span>
    </div>
  </div>
  </Transition>
</template>

<style scoped>
.nnz-drop {
  position: fixed;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  --ground: clamp(18px, 3.2vh, 72px);
}
.board {
  position: absolute;
  top: 2.5vh;
  right: 2vw;
  width: clamp(200px, 17vw, 560px);
  zoom: 1;
  padding: clamp(10px, 1.2vh, 28px) clamp(12px, 1.1vw, 32px);
}
.board .nnz-chip {
  font-size: clamp(12px, 1.3vh, 28px);
}
.title {
  font-size: clamp(18px, 2.4vh, 52px);
  margin-bottom: 0.4em;
}
.board .nnz-row {
  font-size: clamp(14px, 1.6vh, 34px);
}
.ground {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  height: var(--ground);
}
.platform {
  position: absolute;
  bottom: 0;
  height: 100%;
  box-sizing: border-box;
  background: var(--nnz-surface);
  border: var(--nnz-border);
  border-radius: var(--nnz-radius-inner) var(--nnz-radius-inner) 0 0;
  box-shadow: var(--nnz-shadow);
}
.center {
  position: absolute;
  bottom: 0;
  width: clamp(4px, 0.35vw, 12px);
  height: calc(var(--ground) * 1.6);
  transform: translateX(-50%);
  border-radius: var(--nnz-radius-inner) var(--nnz-radius-inner) 0 0;
  background: var(--nnz-accent);
}
.drop {
  position: absolute;
  bottom: var(--ground);
  transform: translateX(-50%);
  transform-origin: 50% 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  --item: clamp(28px, 5vh, 110px);
}
.chute {
  width: calc(var(--item) * 1.9);
  height: auto;
  overflow: visible;
  transition: opacity var(--nnz-out) ease-in, scale var(--nnz-out) ease-in;
  transform-origin: 50% 100%;
}
.canopy {
  fill: var(--nnz-surface);
  stroke: var(--nnz-line);
  stroke-width: 3;
}
.lines {
  fill: none;
  stroke: var(--nnz-line);
  stroke-width: 2;
}
.payload {
  width: var(--item);
  height: var(--item);
  transition: width var(--nnz-in) var(--nnz-ease), height var(--nnz-in) var(--nnz-ease), opacity var(--nnz-in);
}
.name {
  zoom: 1;
  margin-top: 0.4vh;
  padding: 0.2em 0.7em;
  font-size: clamp(11px, 1.4vh, 30px);
  font-weight: 800;
  white-space: nowrap;
  max-width: 14vw;
  overflow: hidden;
  text-overflow: ellipsis;
  box-shadow: none;
}
.score {
  position: absolute;
  bottom: 100%;
  font-size: clamp(18px, 3vh, 64px);
  opacity: 0;
}
.drop:not(.is-flying) .chute {
  opacity: 0;
  scale: 1 0.1;
  height: 0;
}
.drop:not(.is-flying) .score {
  animation: pop 1800ms var(--nnz-ease) both;
}
.is-large { --item: clamp(48px, 9vh, 200px); }
.is-medium { --item: clamp(38px, 7vh, 155px); }
.is-small { --item: clamp(28px, 5vh, 110px); }
.is-miss { --item: clamp(22px, 4vh, 90px); }
.is-miss .payload,
.is-miss .name {
  opacity: 0.5;
  filter: grayscale(1);
}
.is-large .score {
  color: var(--nnz-accent);
}
@keyframes pop {
  0% { opacity: 0; translate: 0 0; scale: 0.6; }
  20% { opacity: 1; translate: 0 -2vh; scale: 1.15; }
  80% { opacity: 1; translate: 0 -3vh; scale: 1; }
  100% { opacity: 0; translate: 0 -5vh; scale: 1; }
}
</style>
