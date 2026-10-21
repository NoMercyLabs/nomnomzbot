<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
