<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Why a run failed, in one word. It is neutral on purpose: the red dot of the status already marks the failure.
// A run written before the failure shape has no kind and shows nothing here.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { Badge } from '@/components/ui/badge';

const props = defineProps<{ kind: string | null | undefined }>();

const { t, te } = useI18n();

const key = computed<string>(() => `dashboard.admin.console.runs.failureKinds.${props.kind}`);
const label = computed<string>(() =>
  te(key.value)
    ? t(key.value)
    : String(props.kind));
</script>

<template>
  <Badge v-if="kind" data-run-failure-kind variant="secondary">{{ label }}</Badge>
</template>
