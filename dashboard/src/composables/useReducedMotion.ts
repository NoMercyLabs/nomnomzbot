// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createGlobalState, usePreferredReducedMotion } from '@vueuse/core';
import { computed } from 'vue';

// Whether the reader asked the system for less motion. One media listener serves the whole app.
export const useReducedMotion = createGlobalState(() => {
  const motion = usePreferredReducedMotion();

  return computed(() => motion.value === 'reduce');
});
