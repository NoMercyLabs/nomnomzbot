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

import { unrefElement, useScroll, type MaybeComputedElementRef } from '@vueuse/core';
import { computed, type ComputedRef } from 'vue';

// A sideways scroller (a tab strip on a phone) cuts its content at the edge. The edge that still hides
// content fades out, so the cut reads as "swipe for more" instead of a clipped label.
export function useScrollFade(target: MaybeComputedElementRef): ComputedRef<string> {
  const { arrivedState } = useScroll(() => unrefElement(target), { offset: { left: 2, right: 2 } });

  return computed<string>(() => {
    const hiddenLeft = !arrivedState.left;
    const hiddenRight = !arrivedState.right;

    if (hiddenLeft && hiddenRight)
      return 'mask-x-from-90%';

    if (hiddenRight)
      return 'mask-r-from-90%';

    if (hiddenLeft)
      return 'mask-l-from-90%';

    return '';
  });
}
