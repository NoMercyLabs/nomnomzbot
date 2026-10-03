<!-- SPDX-License-Identifier: AGPL-3.0-or-later  (c) NoMercy Labs -->
<script setup lang="ts">
import { ref, reactive, computed, onMounted, onUnmounted } from 'vue'

// The overlay SDK is the typed global `NomNomz`, injected before this bundle runs. Its settings type
// (NnzWidgetSettings) and the payload of each event come from this widget's own SDK types.

// Live poll / prediction bars. Binds the alert vocabulary the dashboard broadcasters use —
// poll_begin/poll_progress/poll_end (PollBeganAlertDto: { pollId, title, choices: [{ id, title, votes,
// channelPointsVotes }] }) and prediction_begin/progress/lock/end (PredictionBeganAlertDto: { predictionId,
// title, outcomes: [{ id, title, channelPoints, users, color }] }). Idle until a round begins; the winner is
// highlighted on end, then the panel hides.
interface PollPredConfig {
  position: string                 // 'left' | 'right'
  colors: { bar?: string; track?: string }
  accentColor: string
}

const cfg = reactive<PollPredConfig>({ position: 'left', colors: {}, accentColor: '#9146ff' })

type PollFrame = NnzWidgetEventMap['poll_begin'] | NnzWidgetEventMap['poll_progress'] | NnzWidgetEventMap['poll_end']
type PredictionFrame =
  | NnzWidgetEventMap['prediction_begin']
  | NnzWidgetEventMap['prediction_progress']
  | NnzWidgetEventMap['prediction_lock']
  | NnzWidgetEventMap['prediction_end']

// The colors setting is free-form JSON (typed unknown); this narrows it to its object shape.
function isObject<T extends object>(v: unknown): v is T {
  return !!v && typeof v === 'object'
}

interface Bar { id: string; label: string; value: number; won: boolean }

const mode = ref<string>('')      // '' idle | 'poll' | 'prediction'
const title = ref<string>('')
const bars = ref<Bar[]>([])
const locked = ref<boolean>(false)
const ended = ref<boolean>(false)
let hideTimer: number | undefined

const total = computed<number>(() => bars.value.reduce((sum: number, b: Bar) => sum + b.value, 0))

function pct(b: Bar): number {
  return total.value > 0 ? Math.round((b.value / total.value) * 100) : 0
}

function show(kind: string, name: string, next: Bar[]): void {
  if (hideTimer) { window.clearTimeout(hideTimer); hideTimer = undefined }
  mode.value = kind
  title.value = name
  bars.value = next
  locked.value = false
  ended.value = false
}

function pollBars(d: PollFrame, winningId: string): Bar[] {
  const choices: PollFrame['choices'] = (d && Array.isArray(d.choices)) ? d.choices : []
  return choices.map((c: PollFrame['choices'][number]) => ({
    id: (c && c.id) || '',
    label: (c && c.title) || '',
    value: (Number(c && c.votes) || 0) + (Number(c && c.channelPointsVotes) || 0),
    won: !!winningId && c && c.id === winningId,
  }))
}

function predictionBars(d: PredictionFrame, winningId: string): Bar[] {
  const outcomes: PredictionFrame['outcomes'] = (d && Array.isArray(d.outcomes)) ? d.outcomes : []
  return outcomes.map((o: PredictionFrame['outcomes'][number]) => ({
    id: (o && o.id) || '',
    label: (o && o.title) || '',
    value: Number(o && o.channelPoints) || 0,
    won: !!winningId && o && o.id === winningId,
  }))
}

function scheduleHide(): void {
  ended.value = true
  hideTimer = window.setTimeout(() => { mode.value = '' }, 8000)
}

function onPollBegin(d: NnzWidgetEventMap['poll_begin']): void { show('poll', (d && d.title) || 'Poll', pollBars(d, '')) }
function onPollProgress(d: NnzWidgetEventMap['poll_progress']): void { show('poll', (d && d.title) || title.value, pollBars(d, '')) }
function onPollEnd(d: NnzWidgetEventMap['poll_end']): void {
  show('poll', (d && d.title) || title.value, pollBars(d, (d && d.winningChoiceId) || ''))
  scheduleHide()
}
function onPredBegin(d: NnzWidgetEventMap['prediction_begin']): void { show('prediction', (d && d.title) || 'Prediction', predictionBars(d, '')) }
function onPredProgress(d: NnzWidgetEventMap['prediction_progress']): void { show('prediction', (d && d.title) || title.value, predictionBars(d, '')) }
function onPredLock(d: NnzWidgetEventMap['prediction_lock']): void {
  show('prediction', (d && d.title) || title.value, predictionBars(d, ''))
  locked.value = true
}
function onPredEnd(d: NnzWidgetEventMap['prediction_end']): void {
  show('prediction', (d && d.title) || title.value, predictionBars(d, (d && d.winningOutcomeId) || ''))
  scheduleHide()
}

onMounted(() => {
  if (typeof NomNomz === 'undefined') return
  NomNomz.onSettings((s: NnzWidgetSettings) => {
    if (!s || typeof s !== 'object') return
    if (typeof s.position === 'string' && s.position) cfg.position = s.position
    if (isObject<PollPredConfig['colors']>(s.colors)) cfg.colors = s.colors
    if (typeof s.accentColor === 'string' && s.accentColor) cfg.accentColor = s.accentColor
  })
  NomNomz.on('poll_begin', onPollBegin)
  NomNomz.on('poll_progress', onPollProgress)
  NomNomz.on('poll_end', onPollEnd)
  NomNomz.on('prediction_begin', onPredBegin)
  NomNomz.on('prediction_progress', onPredProgress)
  NomNomz.on('prediction_lock', onPredLock)
  NomNomz.on('prediction_end', onPredEnd)
})

onUnmounted(() => {
  if (hideTimer) window.clearTimeout(hideTimer)
  if (typeof NomNomz === 'undefined') return
  NomNomz.off('poll_begin', onPollBegin)
  NomNomz.off('poll_progress', onPollProgress)
  NomNomz.off('poll_end', onPollEnd)
  NomNomz.off('prediction_begin', onPredBegin)
  NomNomz.off('prediction_progress', onPredProgress)
  NomNomz.off('prediction_lock', onPredLock)
  NomNomz.off('prediction_end', onPredEnd)
})
</script>

<template>
  <div
    v-if="mode"
    class="nnz-pollpred"
    :class="'pos-' + cfg.position"
    :style="{
      '--accent': cfg.accentColor,
      '--bar': cfg.colors.bar || cfg.accentColor,
      '--track': cfg.colors.track || 'rgba(255,255,255,0.14)',
    }"
  >
    <div class="head">
      <span class="kind">{{ mode === 'poll' ? 'POLL' : 'PREDICTION' }}</span>
      <span v-if="locked" class="state">LOCKED</span>
      <span v-else-if="ended" class="state">RESULT</span>
    </div>
    <div class="title">{{ title }}</div>
    <div v-for="b in bars" :key="b.id" class="choice" :class="{ won: b.won }">
      <div class="row">
        <span class="label">{{ b.label }}</span>
        <span class="value">{{ pct(b) }}%</span>
      </div>
      <div class="track"><div class="fill" :style="{ width: pct(b) + '%' }"></div></div>
    </div>
  </div>
</template>

<style scoped>
.nnz-pollpred {
  position: fixed;
  top: 50%;
  transform: translateY(-50%);
  width: min(340px, 32vw);
  padding: 14px 16px;
  border-radius: 12px;
  color: #fff;
  background: rgba(12, 12, 18, 0.86);
  border: 1px solid color-mix(in srgb, var(--accent, #9146ff) 45%, transparent);
  box-shadow: 0 6px 24px rgba(0, 0, 0, 0.45);
  font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
  pointer-events: none;
}
.pos-left {
  left: 16px;
}
.pos-right {
  right: 16px;
}
.head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.kind {
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 1.5px;
  color: var(--accent, #9146ff);
}
.state {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 1px;
  opacity: 0.85;
}
.title {
  margin: 6px 0 10px;
  font-size: 16px;
  font-weight: 700;
  line-height: 1.3;
}
.choice {
  margin-top: 8px;
}
.choice.won .label,
.choice.won .value {
  color: var(--accent, #9146ff);
  font-weight: 800;
}
.row {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
}
.label {
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.value {
  flex: none;
  font-family: ui-monospace, monospace;
}
.track {
  margin-top: 4px;
  height: 8px;
  border-radius: 4px;
  background: var(--track);
  overflow: hidden;
}
.fill {
  height: 100%;
  border-radius: 4px;
  background: var(--bar);
  transition: width 0.4s ease;
}
</style>
