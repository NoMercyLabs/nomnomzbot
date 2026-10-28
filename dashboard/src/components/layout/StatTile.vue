<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
    class="grid min-w-0 content-start"
    :class="plain ? 'gap-0.5' : 'gap-1 rounded-lg border border-border bg-card p-4'"
  >
    <!-- Plain matches the profile card's facts: a small label over a smaller value. -->
    <span class="text-muted-foreground" :class="plain ? 'text-xs' : 'text-sm'">{{ label }}</span>
    <b
      class="font-semibold tabular-nums"
      :class="[plain ? 'text-xl' : 'text-2xl', warn ? 'text-destructive' : '']"
    >{{ shown }}</b>
    <span
      v-if="context"
      data-slot="stat-context"
      class="text-muted-foreground"
      :class="plain ? 'text-xs' : 'text-sm'"
    >{{ context }}</span>
  </div>
</template>
