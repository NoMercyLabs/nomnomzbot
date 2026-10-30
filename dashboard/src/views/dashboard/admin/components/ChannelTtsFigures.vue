<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The TTS figures of the last 24 hours: how many clips, how many failed, the average synthesis time and the
// 95th percentile. Only the failed count takes the red dot, and only when a clip failed, so the accent marks
// the one problem. A time the bot could not measure reads as a dash.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ChannelTtsWindow } from '@/api/adminDiagnostics';
import StatusBadge from '@/components/layout/StatusBadge.vue';

import { synthesisText } from './ttsSynthesis';

const props = defineProps<{ last24Hours: ChannelTtsWindow }>();

const { t, locale } = useI18n();

const clips = computed<number>(() => Number(props.last24Hours.clips));
const failed = computed<number>(() => Number(props.last24Hours.failedClips));
const average = computed<string>(() =>
  synthesisText(props.last24Hours.averageSynthesisMs, locale.value, t('dashboard.admin.console.tts.none')));
const p95 = computed<string>(() =>
  synthesisText(props.last24Hours.p95SynthesisMs, locale.value, t('dashboard.admin.console.tts.none')));
</script>

<template>
  <div class="grid grid-cols-4 gap-4 max-lg:grid-cols-2 max-sm:grid-cols-1">
    <div data-tts-clips class="grid content-start gap-1">
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.clips') }}</p>
      <p class="text-2xl font-semibold tabular-nums">{{ clips }}</p>
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.clipsHint') }}</p>
    </div>
    <div data-tts-failed class="grid content-start gap-1">
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.failed') }}</p>
      <p class="text-2xl font-semibold tabular-nums">{{ failed }}</p>
      <StatusBadge
        v-if="failed > 0"
        tone="bad"
        :label="t('dashboard.admin.console.tts.failedHint', { count: failed, total: clips })"
      />
      <p v-else class="text-sm text-muted-foreground">
        {{ t('dashboard.admin.console.tts.noneFailed') }}
      </p>
    </div>
    <div data-tts-average class="grid content-start gap-1">
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.average') }}</p>
      <p data-tts-value class="text-2xl font-semibold tabular-nums">{{ average }}</p>
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.averageHint') }}</p>
    </div>
    <div data-tts-p95 class="grid content-start gap-1">
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.p95') }}</p>
      <p data-tts-value class="text-2xl font-semibold tabular-nums">{{ p95 }}</p>
      <p class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.p95Hint') }}</p>
    </div>
  </div>
</template>
