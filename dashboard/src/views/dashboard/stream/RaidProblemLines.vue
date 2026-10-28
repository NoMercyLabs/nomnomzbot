<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The translated lines the server gave for one raid rule group, shown next to that group's field. A rule in a
// list is matched by the name the owner gave it (subject); a group without a subject takes every line for it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ApiFieldProblem } from '@/api/errors';

const props = defineProps<{
  problems: ApiFieldProblem[];
  field: string;
  subject?: string;
}>();

const { t } = useI18n();

const lines = computed(() =>
  props.problems
    .filter((problem) => problem.field === props.field)
    .filter((problem) => props.subject === undefined || problem.subject === props.subject)
    .map((problem) =>
      t(`dashboard.stream.raids.problems.${problem.reason}`, {
        value: problem.value ?? '',
        limit: problem.limit ?? '',
      }),
    ),
);
</script>

<template>
  <p
    v-for="line in lines"
    :key="line"
    role="alert"
    :data-raid-problem="field"
    class="text-sm text-destructive"
  >
    {{ line }}
  </p>
</template>
