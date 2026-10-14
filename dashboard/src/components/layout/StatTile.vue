<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One stat: label on top, the value, then one line of context. No icons.
// The value takes color only when it is a warning (warn). A number is grouped in the reader's language.
// plain drops the box for a narrow drawer, where its padding would wrap the context line.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

const props = withDefaults(
  defineProps<{
    label: string;
    value: string | number;
    context?: string;
    warn?: boolean;
    plain?: boolean;
  }>(),
  {
    context: undefined,
    warn: false,
    plain: false,
  },
);

const { n } = useI18n();
const shown = computed<string>(() =>
  typeof props.value === 'number'
    ? n(props.value)
    : props.value,
);
</script>

<template>
  <div
    data-slot="stat-tile"
    class="grid min-w-0 gap-1"
    :class="plain ? '' : 'rounded-lg border border-border bg-card p-4'"
  >
    <span class="text-sm text-muted-foreground">{{ label }}</span>
    <b class="text-2xl font-semibold tabular-nums" :class="warn ? 'text-destructive' : ''">{{
      shown
    }}</b>
    <span
      v-if="context"
      data-slot="stat-context"
      class="text-muted-foreground"
      :class="plain ? 'text-xs' : 'text-sm'"
    >{{ context }}</span>
  </div>
</template>
