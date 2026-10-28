<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One instruction line from the bot. The text stays text, inline: an address to open becomes a link, an address
// to paste (under /api/) becomes text with a small copy button beside it.
import { computed } from 'vue';

import { splitLinks } from '@/lib/text';

import SetupAddress from './SetupAddress.vue';

const props = defineProps<{ line: string }>();

const parts = computed(() => splitLinks(props.line));
</script>

<template>
  <template v-for="(part, index) in parts" :key="index">
    <SetupAddress v-if="part.kind !== 'text'" :url="part.value" :use="part.kind" />
    <template v-else>{{ part.value }}</template>
  </template>
</template>
