<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One instruction line from the bot. The text stays text; each web address in it becomes a row the person can
// open in a new tab or copy.
import { computed } from 'vue';

import { splitLinks } from '@/lib/text';

import LinkActions from './LinkActions.vue';

const props = defineProps<{ line: string }>();

// Punctuation left over after an address ("...dashboard.") would stand alone on a row.
const parts = computed(() =>
  splitLinks(props.line).filter((part) => part.kind === 'link' || /[^\s.,;:!?)\]]/.test(part.value)),
);
</script>

<template>
  <template v-for="(part, index) in parts" :key="index">
    <LinkActions v-if="part.kind === 'link'" :url="part.value" class="my-1" />
    <template v-else>{{ part.value }}</template>
  </template>
</template>
