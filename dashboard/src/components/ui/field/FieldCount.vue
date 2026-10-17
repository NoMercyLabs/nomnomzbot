<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// How many characters a field holds against its limit, shown at the end of the label row.
import type { HTMLAttributes } from 'vue';
import { computed } from 'vue';

import { cn } from '@/lib/utils';

const props = defineProps<{ value: string; max: number; class?: HTMLAttributes['class'] }>();

const full = computed(() => props.value.length >= props.max);
</script>

<template>
  <span
    data-slot="field-count"
    aria-live="polite"
    :class="cn('ml-auto text-sm tabular-nums', full ? 'text-destructive' : 'text-muted-foreground', props.class)"
  >{{ value.length }}/{{ max }}</span>
</template>
