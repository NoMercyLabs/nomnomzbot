<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A chart inside a PanelCard (spec: Chart, Unovis). One plain-text takeaway sits above every chart.
// kind: line and area use time on the x axis when the x values are dates; bar stacks its series, groupedBar puts them side by side.
// Series use the chart tokens (--chart-1 to --chart-5), never a colour of their own.
// The drawing is aria-hidden. A visually hidden table carries the same numbers for screen readers.
import { GroupedBar, StackedBar } from '@unovis/ts'
import {
  VisArea,
  VisAxis,
  VisGroupedBar,
  VisLine,
  VisStackedBar,
  VisXYContainer,
} from '@unovis/vue'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import {
  ChartContainer,
  ChartCrosshair,
  ChartLegendContent,
  ChartTooltip,
  type ChartConfig,
} from '@/components/ui/chart'

export type ChartKind = 'line' | 'area' | 'bar' | 'groupedBar'
export type ChartToken = 'chart-1' | 'chart-2' | 'chart-3' | 'chart-4' | 'chart-5'
export type ChartRow = Record<string, string | number | Date>
export interface ChartSeries {
  key: string
  labelKey: string
  color: ChartToken
}

const props = withDefaults(
  defineProps<{
    title: string
    description?: string
    kind: ChartKind
    data: ChartRow[]
    x: string
    series: ChartSeries[]
    takeaway: string
    state?: PanelState
    emptyText?: string
    errorText?: string
    tier?: 'lead' | 'default' | 'quiet'
  }>(),
  {
    description: undefined,
    state: 'ready',
    emptyText: undefined,
    errorText: undefined,
    tier: 'default',
  },
)

defineEmits<{ retry: [] }>()

const { t, locale } = useI18n()

const panelState = computed(() =>
  props.state === 'ready' && props.data.length === 0
    ? 'empty'
    : props.state,
)

const config: ComputedRef<ChartConfig> = computed(() =>
  Object.fromEntries(
    props.series.map((entry: ChartSeries) => [
      entry.key,
      { label: t(entry.labelKey), color: `var(--${entry.color})` },
    ]),
  ),
)

const numbers = computed(() => new Intl.NumberFormat(locale.value))
const compactNumbers: ComputedRef<Intl.NumberFormat> = computed(
  () => new Intl.NumberFormat(locale.value, { notation: 'compact' }),
)
const fullDates: ComputedRef<Intl.DateTimeFormat> = computed(
  () => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium' }),
)
const shortDates: ComputedRef<Intl.DateTimeFormat> = computed(
  () => new Intl.DateTimeFormat(locale.value, { day: 'numeric', month: 'short' }),
)

const isTime = computed(
  () =>
    (props.kind === 'line' || props.kind === 'area') && props.data[0]?.[props.x] instanceof Date,
)
const isBar = computed(() => props.kind === 'bar' || props.kind === 'groupedBar')

function label(value: string | number | Date | undefined, formatter: Intl.DateTimeFormat): string {
  return value instanceof Date
    ? formatter.format(value)
    : String(value ?? '')
}

const xAccessor = (row: ChartRow, index: number): number => {
  const value = row[props.x]

  return isTime.value && value instanceof Date
    ? value.getTime()
    : index
}

const yAccessors = computed(() =>
  props.series.map(
    (entry: ChartSeries) =>
      (row: ChartRow): number =>
        Number(row[entry.key] ?? 0),
  ),
)
const colors = computed(() => props.series.map((entry: ChartSeries) => `var(--color-${entry.key})`))

function xTick(tick: number | Date): string {
  const at = tick instanceof Date
    ? tick.getTime()
    : tick

  if (isTime.value)
    return shortDates.value.format(new Date(at))

  return Number.isInteger(at)
    ? label(props.data[at]?.[props.x], shortDates.value)
    : ''
}

function yTick(tick: number | Date): string {
  return compactNumbers.value.format(tick instanceof Date
    ? tick.getTime()
    : tick)
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (char: string) => `&#${char.charCodeAt(0)};`)
}

// Unovis wants the tooltip as an HTML string. The look matches ChartTooltipContent.
function tooltip(row: ChartRow): string {
  const lines: string = props.series
    .map(
      (entry: ChartSeries) =>
        `<div class="flex items-center justify-between gap-4">` +
        `<span class="flex items-center gap-2 text-muted-foreground">` +
        `<i class="size-2.5 shrink-0 rounded-xs" style="background:var(--color-${entry.key})"></i>` +
        `${escapeHtml(t(entry.labelKey))}</span>` +
        `<span class="font-medium tabular-nums text-foreground">${numbers.value.format(Number(row[entry.key] ?? 0))}</span>` +
        `</div>`,
    )
    .join('')

  return (
    `<div class="grid min-w-32 gap-1.5 rounded-lg border border-border/50 bg-background px-2.5 py-1.5 text-xs shadow-xl">` +
    `<div class="font-medium">${escapeHtml(label(row[props.x], fullDates.value))}</div>${lines}</div>`
  )
}

const barTriggers: ComputedRef<Record<string, (row: ChartRow) => string>> = computed(() => ({
  [props.kind === 'bar'
    ? StackedBar.selectors.bar
    : GroupedBar.selectors.bar]: tooltip,
}))
const yDomain: ComputedRef<[number | undefined, number | undefined]> = computed(() =>
  props.kind === 'line'
    ? [undefined, undefined]
    : [0, undefined],
)
</script>

<template>
  <PanelCard
    :title="title"
    :description="description"
    :tier="tier"
    :state="panelState"
    :empty-text="emptyText"
    :error-text="errorText"
    pad
    @retry="$emit('retry')"
  >
    <template v-if="$slots.actions" #actions><slot name="actions" /></template>
    <div class="flex flex-col gap-3">
      <p class="text-sm text-foreground">{{ takeaway }}</p>
      <ChartContainer :config="config" class="aspect-auto h-52 w-full" aria-hidden="true">
        <VisXYContainer :data="data" :y-domain="yDomain" :padding="{ top: 8, right: 8 }">
          <VisArea
            v-if="kind === 'area'"
            :x="xAccessor"
            :y="yAccessors"
            :color="colors"
            :opacity="0.2"
          />
          <VisLine
            v-if="kind === 'line' || kind === 'area'"
            :x="xAccessor"
            :y="yAccessors"
            :color="colors"
          />
          <VisStackedBar
            v-if="kind === 'bar'"
            :x="xAccessor"
            :y="yAccessors"
            :color="colors"
            :rounded-corners="2"
          />
          <VisGroupedBar
            v-if="kind === 'groupedBar'"
            :x="xAccessor"
            :y="yAccessors"
            :color="colors"
            :rounded-corners="2"
          />
          <VisAxis
            type="x"
            :tick-format="xTick"
            :num-ticks="isBar ? data.length : 6"
            :grid-line="false"
            :domain-line="false"
          />
          <VisAxis type="y" :tick-format="yTick" :num-ticks="4" :domain-line="false" />
          <ChartCrosshair
            v-if="!isBar"
            :x="xAccessor"
            :y="yAccessors"
            :color="colors"
            :template="tooltip"
          />
          <ChartTooltip v-else :triggers="barTriggers" />
        </VisXYContainer>
      </ChartContainer>
      <ChartContainer
        v-if="series.length > 1"
        :config="config"
        class="aspect-auto h-auto"
        aria-hidden="true"
      >
        <ChartLegendContent class="pt-0" />
      </ChartContainer>
      <table class="sr-only">
        <caption>
          {{
            title
          }}
        </caption>
        <thead>
          <tr>
            <th scope="col">{{ t('common.chart.period') }}</th>
            <th v-for="entry in series" :key="entry.key" scope="col">{{ t(entry.labelKey) }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, index) in data" :key="index">
            <th scope="row">{{ label(row[x], fullDates) }}</th>
            <td v-for="entry in series" :key="entry.key">
              {{ numbers.format(Number(row[entry.key] ?? 0)) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </PanelCard>
</template>
