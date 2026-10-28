<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The view-only scope: while readOnly is true, every native control inside is disabled by the
// browser (a disabled fieldset) and the block's title says why. Shadcn buttons ignore the pointer
// when disabled, so a hover reaches the fieldset and the title still shows.
// A nested scope inherits a locked parent: a child can never unlock what a parent locked.
// Descendants, and portal dialogs below, read the state with useReadOnly().
import { computed, provide } from 'vue';

import { readOnlyKey, useReadOnly } from '@/components/layout/readOnly';

const props = withDefaults(
  defineProps<{
    readOnly: boolean;
    reason?: string;
  }>(),
  { reason: undefined },
);

const inherited = useReadOnly();
const locked = computed(() => inherited.value || props.readOnly);

provide(readOnlyKey, locked);
</script>

<template>
  <fieldset
    class="m-0 min-w-0 border-0 p-0"
    data-slot="read-only-scope"
    :disabled="locked"
    :title="locked ? (reason ?? $t('common.readOnly')) : undefined"
  >
    <slot />
  </fieldset>
</template>
