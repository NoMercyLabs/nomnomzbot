<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The page-level actions (Save, Discard, a pager). From the tablet width up they stay where the page puts them;
// below it they move into the frame's one sticky footer above the bottom bar, so they stay in reach while the
// page scrolls. Owner rule 2026-10-21: "a sticky action footer for page-level actions".
import { computed } from 'vue';

import { useScreenFrom } from '@/composables/useLayout';

const tablet = useScreenFrom('tablet');
const moved = computed<boolean>(() => !tablet.value);
</script>

<template>
  <Teleport defer to="[data-slot=frame-action-footer]" :disabled="!moved">
    <div
      data-slot="page-action-footer"
      class="flex flex-wrap items-center gap-2"
      :class="moved ? 'border-t border-border bg-background px-4 py-3' : ''"
    >
      <slot />
    </div>
  </Teleport>
</template>
