<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The designer's search icon drawn as a 2px line. Its file is a 24 box with a 1.5 stroke, so the
// stroke is scaled to the rendered size: 24 / 16 * 2 = 3 at 16px, 24 / 20 * 2 = 2.4 at 20px.
import SearchMagnifyingGlass from '~icons/nnz/search-magnifying-glass';

import { cn } from '@/lib/utils';

const STROKE = {
  4: '[&_path]:stroke-3',
  5: '[&_path]:stroke-[2.4]',
} as const satisfies Record<4 | 5, string>;

// 4 is size-4 (16px, the input-group default); 5 is size-5 (20px, an icon button).
const props = withDefaults(defineProps<{ size?: keyof typeof STROKE }>(), { size: 4 });
</script>

<template>
  <SearchMagnifyingGlass :class="cn(STROKE[props.size], props.size === 5 && 'size-5')" />
</template>
