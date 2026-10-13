<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A small step in place of a control (spec row InlineFlow; mockup inline-flow.js:14-26).
// The default slot is the opener. While a step shows, it sits on top of the opener's space,
// so nothing on the page moves. Esc backs out and focus returns to the opener.
// confirm: ask, then run on the second press. apply / run: run at once. note: one plain line.
// failure: the reason to show when the action rejects; without it a plain "did not work" line shows.
// harm: the confirm button is red only when the action destroys something.
// The step is wider than one button, so wrap every control it replaces in the slot; right anchors
// it to the right edge for a group at the end of a row.
import { onKeyStroke } from '@vueuse/core'
import { nextTick, ref, watch, type Ref } from 'vue'
import Check from '~icons/nnz/check'

import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'
import {
  useInlineFlow,
  type InlineFlowKind,
  type InlineFlowStep,
} from '@/composables/useInlineFlow'

const props = withDefaults(
  defineProps<{
    kind: InlineFlowKind
    label: string
    ask?: string
    yes?: string
    text?: string
    result?: string
    failure?: string
    harm?: boolean
    right?: boolean
    action?: () => void | Promise<void>
    revert?: () => void | Promise<void>
    undoMs?: number
  }>(),
  {
    ask: undefined,
    yes: undefined,
    text: undefined,
    result: undefined,
    failure: undefined,
    harm: false,
    right: false,
    action: undefined,
    revert: undefined,
    undoMs: undefined,
  },
)

const { step, canUndo, start, confirm, undo, back } = useInlineFlow({
  kind: () => props.kind,
  action: () => props.action,
  revert: () => props.revert,
  undoMs: () => props.undoMs,
})

const root: Ref<HTMLElement | null> = ref(null)
const opener: Ref<HTMLElement | null> = ref(null)
const box: Ref<HTMLElement | null> = ref(null)

onKeyStroke('Escape', back, { target: root })

// Focus follows the step: the safe button first, and the opener again when the step ends.
watch(step, async (now: InlineFlowStep, before: InlineFlowStep) => {
  await nextTick()
  if (now === 'idle') {
    if (before !== 'idle')
      opener.value?.querySelector<HTMLElement>('button, [href], [tabindex]')?.focus()
    return
  }
  const target: string = now === 'ask' ? 'back' : canUndo.value ? 'undo' : 'back'
  box.value?.querySelector<HTMLElement>(`[data-flow-act="${target}"]`)?.focus()
})
</script>

<template>
  <span
    ref="root"
    data-slot="inline-flow"
    :data-step="step"
    class="relative inline-flex items-center align-middle"
  >
    <span
      ref="opener"
      data-slot="inline-flow-opener"
      class="inline-flex items-center"
      :class="{ invisible: step !== 'idle' }"
      :inert="step === 'idle' ? undefined : true"
      @click="start"
    >
      <slot />
    </span>
    <span
      v-if="step !== 'idle'"
      ref="box"
      data-slot="inline-flow-box"
      role="group"
      :aria-label="label"
      :class="right ? 'right-0' : 'left-0'"
      class="absolute inset-y-0 z-10 flex items-center gap-2 text-sm whitespace-nowrap text-foreground"
    >
      <template v-if="step === 'ask'">
        <span>{{ ask }}</span>
        <Button
          data-flow-act="ok"
          size="sm"
          :variant="harm ? 'destructive' : 'default'"
          @click="confirm"
        >
          {{ yes ?? $t('common.flow.yes') }}
        </Button>
        <Button data-flow-act="back" size="sm" variant="ghost" @click="back">{{
          $t('common.cancel')
        }}</Button>
      </template>
      <template v-else-if="step === 'pending'">
        <Spinner />
        <span>{{ text ?? $t('common.flow.working') }}</span>
      </template>
      <template v-else-if="step === 'failed'">
        <span class="text-destructive">{{ failure ?? $t('common.flow.failed') }}</span>
        <Button data-flow-act="back" size="sm" variant="ghost" @click="back">{{
          $t('common.flow.done')
        }}</Button>
      </template>
      <template v-else>
        <span class="inline-flex items-center gap-2">
          <Check v-if="kind !== 'note'" class="size-4 flex-none" aria-hidden="true" />
          {{ kind === 'note' ? text : (result ?? $t('common.flow.done')) }}
        </span>
        <Button v-if="canUndo" data-flow-act="undo" size="sm" variant="outline" @click="undo">
          {{ $t('common.flow.undo') }}
        </Button>
        <Button data-flow-act="back" size="sm" variant="ghost" @click="back">{{
          $t('common.flow.done')
        }}</Button>
      </template>
    </span>
    <span class="sr-only" role="status" aria-live="polite">
      <template v-if="step === 'ask'">{{ ask }}</template>
      <template v-else-if="step === 'pending'">{{ text ?? $t('common.flow.working') }}</template>
      <template v-else-if="step === 'failed'">{{ failure ?? $t('common.flow.failed') }}</template>
      <template v-else-if="step === 'done'">{{
        kind === 'note' ? text : (result ?? $t('common.flow.done'))
      }}</template>
    </span>
  </span>
</template>
