<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// How a run ended, as a dot and words. Only a run that failed or timed out gets the red dot, so the accent
// marks the worst problem of the list. An outcome the bot adds later shows as the bot names it.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { lookup } from '@/lib/array';

const props = defineProps<{ status: string }>();

const TONES = {
  success: 'ok',
  failed: 'bad',
  timeout: 'bad',
  denied: 'off',
} as const satisfies Record<string, StatusTone>;

const { t, te } = useI18n();

const key = computed<string>(() => `dashboard.admin.console.runs.status.${props.status}`);
const label = computed<string>(() =>
  te(key.value)
    ? t(key.value)
    : props.status);
</script>

<template>
  <StatusBadge data-run-status :tone="lookup(TONES, status) ?? 'off'" :label="label" />
</template>
