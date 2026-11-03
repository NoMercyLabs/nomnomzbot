// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// Drag for the steps of one lane. Every lane (the top level, each branch of a check, a loop, a group) is a
// Sortable in one shared group, so a step crosses lanes. Sortable moves the node itself; it goes back so Vue
// alone orders the cards from the draft, and the drop reaches the draft as one move.
import Sortable, { type SortableEvent } from 'sortablejs';
import type { Directive } from 'vue';

import type { Spot } from './stepEdits';

export type LaneDrag = {
  parentId: string | null;
  branch: string | null;
  enabled: boolean;
  onMove: (id: string, spot: Spot) => void;
};

const lanes = new WeakMap<Element, LaneDrag>();

// Sortable counts only the cards, so a lane holding just its "nothing extra" line still takes a drop at 0.
function dropped(event: SortableEvent): void {
  const { item, from, to } = event;

  item.remove();
  from.insertBefore(item, from.children[event.oldIndex ?? 0] ?? null);

  const lane = lanes.get(to);
  const id = item.dataset.stepId;

  if (!lane || !id || event.newIndex === undefined) return;

  lane.onMove(id, {
    parentId: lane.parentId,
    branch: lane.branch,
    order: event.newIndex,
  });
}

// A long press on the handle starts a touch drag, so a finger that starts anywhere else still scrolls.
export const vLaneDrag = {
  mounted(el, binding) {
    lanes.set(el, binding.value);

    Sortable.create(el, {
      group: 'pipeline-steps',
      draggable: '[data-step]',
      handle: '[data-step-handle]',
      animation: 150,
      delay: 250,
      delayOnTouchOnly: true,
      touchStartThreshold: 6,
      fallbackOnBody: true,
      swapThreshold: 0.65,
      ghostClass: 'step-ghost',
      chosenClass: 'step-chosen',
      disabled: !binding.value.enabled,
      onEnd: dropped,
    });
  },
  updated(el, binding) {
    lanes.set(el, binding.value);
    Sortable.get(el)?.option('disabled', !binding.value.enabled);
  },
  unmounted(el) {
    lanes.delete(el);
    Sortable.get(el)?.destroy();
  },
} as const satisfies Directive<HTMLElement, LaneDrag>;
