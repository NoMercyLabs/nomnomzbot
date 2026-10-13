// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The state behind InlineFlow (spec row InlineFlow + useInlineFlow).
// confirm: ask first, run on the second press. apply: run at once. run: like apply, for work that
// takes a moment. note: one plain line, nothing to run.
// After the work finishes the result stays; with a revert handler it also offers Undo for undoMs.
import { useTimeoutFn } from '@vueuse/core'
import { computed, ref, toValue, type ComputedRef, type MaybeRefOrGetter, type Ref } from 'vue'

export type InlineFlowKind = 'confirm' | 'apply' | 'run' | 'note'
export type InlineFlowStep = 'idle' | 'ask' | 'pending' | 'done' | 'failed'
type Work = () => void | Promise<void>

export interface InlineFlowOptions {
  kind: MaybeRefOrGetter<InlineFlowKind>
  action?: MaybeRefOrGetter<Work | undefined>
  revert?: MaybeRefOrGetter<Work | undefined>
  undoMs?: MaybeRefOrGetter<number | undefined>
}

export interface InlineFlow {
  step: Ref<InlineFlowStep>
  canUndo: ComputedRef<boolean>
  start: () => void
  confirm: () => Promise<void>
  undo: () => Promise<void>
  back: () => void
}

export const DEFAULT_UNDO_MS = 6000

export function useInlineFlow(options: InlineFlowOptions): InlineFlow {
  const step: Ref<InlineFlowStep> = ref('idle')
  const canUndo: ComputedRef<boolean> = computed(
    () => step.value === 'done' && toValue(options.revert) !== undefined,
  )

  const undoWindow = useTimeoutFn(
    () => {
      if (step.value === 'done') step.value = 'idle'
    },
    () => toValue(options.undoMs) ?? DEFAULT_UNDO_MS,
    { immediate: false },
  )

  async function perform(): Promise<void> {
    step.value = 'pending'
    try {
      await toValue(options.action)?.()
    } catch {
      step.value = 'failed'
      return
    }
    step.value = 'done'
    if (toValue(options.revert) !== undefined) undoWindow.start()
  }

  function start(): void {
    if (step.value !== 'idle') return
    const kind: InlineFlowKind = toValue(options.kind)
    if (kind === 'note') step.value = 'done'
    else if (kind === 'confirm') step.value = 'ask'
    else void perform()
  }

  async function confirm(): Promise<void> {
    if (step.value === 'ask') await perform()
  }

  async function undo(): Promise<void> {
    const revert: Work | undefined = toValue(options.revert)
    if (!canUndo.value || revert === undefined) return
    undoWindow.stop()
    step.value = 'pending'
    try {
      await revert()
    } catch {
      step.value = 'failed'
      return
    }
    step.value = 'idle'
  }

  // Backs out of a question, a result or a failure. It never runs the action and never undoes it.
  function back(): void {
    if (step.value === 'pending' || step.value === 'idle') return
    undoWindow.stop()
    step.value = 'idle'
  }

  return { step, canUndo, start, confirm, undo, back }
}
