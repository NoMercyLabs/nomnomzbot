<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
