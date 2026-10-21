<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
