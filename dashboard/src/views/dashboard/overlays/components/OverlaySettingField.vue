<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One settings field: label and "key . type . help" on the left, the control its type asks for,
// and Reset when the value differs from the default (12-overlays.html:304-313).
// A type the dashboard does not know shows its value read-only.
import RotateCcw from '~icons/nnz/arrow-counter-clockwise'
import { computed, useId, type ComputedRef } from 'vue'

import type { WidgetSettingsField } from '@/api/widgets'
import MultiChoice from '@/components/layout/MultiChoice.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

import { clampNumber, humanize, useSettingText, type SettingText } from './settingField'

const props = defineProps<{ field: WidgetSettingsField; value: unknown; changed: boolean }>()
const emit = defineEmits<{ update: [value: unknown]; reset: [] }>()

const text: SettingText = useSettingText()
const titleId: string = useId()

const label: ComputedRef<string> = computed<string>(() =>
  text(props.field.label, humanize(props.field.key)),
)
const meta: ComputedRef<string> = computed<string>(() => {
  const help: string | null = props.field.help ? text(props.field.help, '') : null
  return [props.field.key, props.field.type, help].filter(Boolean).join(' · ')
})

interface Choice {
  value: string
  label: string
}
const choices: ComputedRef<Choice[]> = computed<Choice[]>(() =>
  (props.field.options ?? []).map((option): Choice => ({
    value: option.value,
    label: text(option.label, humanize(option.value)),
  })),
)
const chosenLabel: ComputedRef<string> = computed<string>(
  () =>
    choices.value.find((choice: Choice) => choice.value === props.value)?.label ??
    String(props.value ?? ''),
)
const picked: ComputedRef<string[]> = computed<string[]>(() =>
  Array.isArray(props.value) ? props.value.map(String) : [],
)
const shown: ComputedRef<string> = computed<string>(() =>
  props.value === null || props.value === undefined ? '' : String(props.value),
)
const known: ComputedRef<boolean> = computed<boolean>(() =>
  ['bool', 'number', 'text', 'color', 'select', 'multiselect'].includes(props.field.type),
)

// The number is held inside min and max on change; the box shows the held value even when it did not move.
function onNumber(event: Event): void {
  const box: HTMLInputElement = event.target as HTMLInputElement
  const next: unknown = clampNumber(box.value, props.field, props.value)
  box.value = String(next)
  emit('update', next)
}
</script>

<template>
  <div
    :data-setting="field.key"
    :data-changed="changed"
    class="flex flex-wrap items-center gap-x-3 gap-y-2"
    :class="field.type === 'bool' ? 'pr-3' : 'px-3 py-2'"
  >
    <SettingRow
      v-if="field.type === 'bool'"
      class="min-w-0 flex-1"
      :model-value="value === true"
      :title="label"
      :description="meta"
      @update:model-value="emit('update', $event)"
    />
    <template v-else>
      <span class="min-w-40 flex-1">
        <span :id="titleId" class="flex items-center gap-2 text-sm font-medium wrap-break-word">
          {{ label }}
          <span
            v-if="changed"
            class="size-1.5 flex-none rounded-full bg-muted-foreground"
            :title="$t('dashboard.overlays.detail.settings.changed')"
          />
          <span v-if="changed" class="sr-only">{{
            $t('dashboard.overlays.detail.settings.changed')
          }}</span>
        </span>
        <span class="block text-sm text-muted-foreground wrap-break-word">{{ meta }}</span>
      </span>

      <Input
        v-if="field.type === 'number'"
        type="number"
        class="w-full sm:w-48"
        :min="field.min ?? undefined"
        :max="field.max ?? undefined"
        :step="field.step ?? undefined"
        :model-value="shown"
        :aria-labelledby="titleId"
        @change="onNumber"
      />
      <span v-else-if="field.type === 'color'" class="relative w-full sm:w-48">
        <span
          data-swatch
          class="absolute top-1/2 left-2 size-5 -translate-y-1/2 rounded-xs border border-border"
          :style="{ backgroundColor: shown }"
          aria-hidden="true"
        />
        <Input
          class="pl-9 font-mono"
          :model-value="shown"
          :aria-labelledby="titleId"
          @update:model-value="emit('update', String($event))"
        />
      </span>
      <Input
        v-else-if="field.type === 'text'"
        class="w-full sm:w-48"
        :model-value="shown"
        :aria-labelledby="titleId"
        @update:model-value="emit('update', String($event))"
      />
      <Select
        v-else-if="field.type === 'select'"
        :model-value="shown"
        @update:model-value="emit('update', $event)"
      >
        <SelectTrigger class="w-full sm:w-48" :aria-labelledby="titleId">
          <SelectValue>{{ chosenLabel }}</SelectValue>
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="choice in choices" :key="choice.value" :value="choice.value">
            {{ choice.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <MultiChoice
        v-else-if="field.type === 'multiselect'"
        :options="choices"
        :model-value="picked"
        :aria-labelledby="titleId"
        @update:model-value="emit('update', $event)"
      />
      <Textarea
        v-else
        readonly
        class="w-full font-mono"
        :model-value="
          field.type === 'json' || !known ? JSON.stringify(value ?? null, null, 2) : shown
        "
        :aria-labelledby="titleId"
      />
    </template>

    <span class="flex size-8 flex-none items-center justify-center">
      <Button
        v-if="changed && known"
        data-reset
        variant="ghost"
        size="icon-sm"
        :aria-label="$t('dashboard.overlays.detail.settings.reset', { name: label })"
        :title="$t('dashboard.overlays.detail.settings.reset', { name: label })"
        @click="emit('reset')"
      >
        <RotateCcw aria-hidden="true" />
      </Button>
    </span>
  </div>
</template>
