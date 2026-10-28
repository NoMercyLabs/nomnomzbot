<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// The stage: a full-canvas, transparent layer for one-off effects. Driven by the "stage_effect" widget event —
// { preset, text, durationMs, intensity, color }. Presets: run_across, confetti, shake, flash. The widget renders
// nothing while idle; each effect removes its own elements when its duration ends. Everything is CSS animation:
// no external script, no external host. The text only ever goes through a text node ({{ }}), never innerHTML,
// so a viewer's words cannot inject markup.
const cfg = reactive<NnzWidgetSettings>({
  intensity: 5,
  accentColor: '#9146ff',
})

interface Piece {
  x: number
  size: number
  drift: number
  spin: number
  delay: number
  dur: number
  color: string
}

interface Effect {
  id: number
  preset: string
  text: string
  color: string
  intensity: number
  durationMs: number
  pieces: Piece[]
}

const HEX_COLOR = /^#[0-9a-fA-F]{3,8}$/
const CONFETTI_COLORS: string[] = ['#ffffff', '#ffd23f', '#3bceac', '#ee4266']
// What a run_across shows when it carries no text: a running person emoji.
const RUNNER_EMOJI: string = '\u{1F3C3}'

const effects = ref<Effect[]>([])
const timers = new Map<number, number>()
let nextId = 0

function clampIntensity(value: unknown): number {
  const n: number = Number(value)
  return isFinite(n) && n >= 1 ? Math.min(10, Math.round(n)) : cfg.intensity
}

function pickColor(value: unknown): string {
  return typeof value === 'string' && HEX_COLOR.test(value) ? value : cfg.accentColor
}

function makePieces(color: string, intensity: number): Piece[] {
  const palette: string[] = [color, ...CONFETTI_COLORS]
  const count: number = 20 + intensity * 10
  const pieces: Piece[] = []
  for (let i = 0; i < count; i++) {
    pieces.push({
      x: Math.random() * 100,
      size: 6 + Math.random() * 7,
      drift: (Math.random() - 0.5) * 24,
      spin: 180 + Math.random() * 540,
      // Delay 0-40% and run 40-60% of the duration, so every piece is gone when the effect ends.
      delay: Math.random() * 0.4,
      dur: 0.4 + Math.random() * 0.2,
      color: palette[i % palette.length],
    })
  }
  return pieces
}

function remove(id: number): void {
  timers.delete(id)
  effects.value = effects.value.filter((e: Effect) => e.id !== id)
}

function onEffect(d: NnzWidgetEventMap['stage_effect'] | null | undefined): void {
  const data: Partial<NnzWidgetEventMap['stage_effect']> = d || {}
  const preset: string = typeof data.preset === 'string' ? data.preset : ''
  if (!preset) return
  const durationMs: number = isFinite(Number(data.durationMs)) && Number(data.durationMs) > 0
    ? Number(data.durationMs)
    : 3000
  const color: string = pickColor(data.color)
  const intensity: number = clampIntensity(data.intensity)
  const id: number = nextId++
  effects.value = [
    ...effects.value,
    {
      id,
      preset,
      text: typeof data.text === 'string' ? data.text : '',
      color,
      intensity,
      durationMs,
      pieces: preset === 'confetti' ? makePieces(color, intensity) : [],
    },
  ]
  timers.set(id, window.setTimeout(() => remove(id), durationMs))
}

// The shake moves the whole stage layer (and so every effect on it), never the stream underneath. The rumble
// frame is the visible part: an empty transparent layer would shake unseen.
const shaking = computed<Effect | undefined>(() =>
  effects.value.find((e: Effect) => e.preset === 'shake'),
)

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.intensity === 'number' && s.intensity >= 1) cfg.intensity = s.intensity
    if (typeof s.accentColor === 'string' && HEX_COLOR.test(s.accentColor)) cfg.accentColor = s.accentColor
  })
  NomNomz.on('stage_effect', onEffect)
})

onUnmounted(() => {
  timers.forEach((handle: number) => window.clearTimeout(handle))
  timers.clear()
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('stage_effect', onEffect)
})
</script>

<template>
  <div
    v-if="effects.length > 0"
    class="nnz-stage"
    :class="{ shaking: !!shaking }"
    :style="{ '--amp': shaking ? shaking.intensity * 2 : 0 }"
  >
    <template v-for="fx in effects" :key="fx.id">
      <div
        v-if="fx.preset === 'run_across'"
        class="runner"
        :style="{
          color: fx.color,
          fontSize: 3 + fx.intensity * 0.6 + 'rem',
          animationDuration: fx.durationMs + 'ms',
        }"
      >
        <span>{{ fx.text || RUNNER_EMOJI }}</span>
      </div>

      <div v-else-if="fx.preset === 'confetti'" class="confetti">
        <span
          v-for="(p, i) in fx.pieces"
          :key="i"
          class="piece"
          :style="{
            left: p.x + '%',
            width: p.size + 'px',
            height: p.size * 1.6 + 'px',
            background: p.color,
            animationDelay: p.delay * fx.durationMs + 'ms',
            animationDuration: p.dur * fx.durationMs + 'ms',
            '--drift': p.drift + 'vw',
            '--spin': p.spin + 'deg',
          }"
        ></span>
      </div>

      <div
        v-else-if="fx.preset === 'shake'"
        class="rumble"
        :style="{ '--amp': fx.intensity * 2, '--tint': fx.color, animationDuration: fx.durationMs + 'ms' }"
      ></div>

      <div
        v-else-if="fx.preset === 'flash'"
        class="flash"
        :style="{
          background: fx.color,
          '--peak': Math.min(0.95, 0.15 + fx.intensity * 0.08),
          animationDuration: fx.durationMs + 'ms',
        }"
      ></div>
    </template>
  </div>
</template>

<style scoped>
.nnz-stage {
  position: fixed;
  inset: 0;
  overflow: hidden;
  pointer-events: none;
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
}
.nnz-stage.shaking {
  animation: nnz-stage-shake 0.1s linear infinite;
}
@keyframes nnz-stage-shake {
  0% { transform: translate(calc(var(--amp) * 1px), calc(var(--amp) * -0.5px)); }
  25% { transform: translate(calc(var(--amp) * -1px), calc(var(--amp) * 0.5px)); }
  50% { transform: translate(calc(var(--amp) * 0.5px), calc(var(--amp) * 1px)); }
  75% { transform: translate(calc(var(--amp) * -0.5px), calc(var(--amp) * -1px)); }
  100% { transform: translate(calc(var(--amp) * 1px), calc(var(--amp) * -0.5px)); }
}

/* run_across: from the left edge to the right edge once. */
.runner {
  position: absolute;
  left: 0;
  top: 55%;
  white-space: nowrap;
  font-weight: 800;
  line-height: 1;
  text-shadow: 0 4px 16px rgba(0, 0, 0, 0.55);
  transform: translateX(-100%);
  animation-name: nnz-run-across;
  animation-timing-function: linear;
  animation-fill-mode: forwards;
}
@keyframes nnz-run-across {
  from { transform: translateX(-100%); }
  to { transform: translateX(100vw); }
}

/* confetti: pieces fall from above the top edge to below the bottom edge. */
.confetti {
  position: absolute;
  inset: 0;
}
.piece {
  position: absolute;
  top: -5vh;
  border-radius: 2px;
  opacity: 0;
  animation-name: nnz-fall;
  animation-timing-function: ease-in;
  animation-fill-mode: both;
}
@keyframes nnz-fall {
  0% { opacity: 1; transform: translate(0, 0) rotate(0deg); }
  100% { opacity: 1; transform: translate(var(--drift), 110vh) rotate(var(--spin)); }
}

/* shake: a tinted frame at the screen edge that shakes with the stage layer and fades out. */
.rumble {
  position: absolute;
  inset: 0;
  box-shadow: inset 0 0 calc(var(--amp) * 6px) calc(var(--amp) * 1px) var(--tint);
  animation-name: nnz-fade-out;
  animation-timing-function: ease-out;
  animation-fill-mode: forwards;
}
@keyframes nnz-fade-out {
  from { opacity: 1; }
  to { opacity: 0; }
}

/* flash: the whole screen lights up fast and fades. */
.flash {
  position: absolute;
  inset: 0;
  opacity: 0;
  animation-name: nnz-flash;
  animation-timing-function: ease-out;
  animation-fill-mode: forwards;
}
@keyframes nnz-flash {
  0% { opacity: 0; }
  12% { opacity: var(--peak); }
  100% { opacity: 0; }
}
</style>
