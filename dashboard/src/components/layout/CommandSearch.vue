<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Placed inside a Command, it hands the typed search out as v-model, so a list the bot searches on its side can
// ask for the matching rows instead of filtering only the rows it already has.
import { watch } from 'vue';

import { useCommand } from '@/components/ui/command';

const search = defineModel<string>({ default: '' });

const { filterState } = useCommand();

watch(() =>
  filterState.search, (typed: string) => {
  search.value = typed;
});
</script>

<template>
  <slot />
</template>
