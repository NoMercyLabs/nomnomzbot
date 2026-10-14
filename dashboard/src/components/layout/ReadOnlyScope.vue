<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
