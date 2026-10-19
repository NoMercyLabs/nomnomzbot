// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
