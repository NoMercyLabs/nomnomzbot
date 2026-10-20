<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One rule inside a RowGroup: its title and help on the left, its control (the slot) on the right.
// The whole row is a label, so a press on the words reaches the control. Radius 8 fits a RowGroup (12 - 4).
import { useId } from 'vue';

import RowText from '@/components/layout/RowText.vue';

defineProps<{
  title: string;
  description?: string;
}>();

const titleId = useId();
</script>

<template>
  <label
    data-slot="rule-row"
    class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2 hover:bg-foreground/5"
  >
    <span class="min-w-0 flex-1">
      <RowText :title="title" :title-id="titleId" :description="description" />
    </span>
    <slot />
  </label>
</template>
