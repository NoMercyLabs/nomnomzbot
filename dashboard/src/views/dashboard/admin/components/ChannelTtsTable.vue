<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The TTS clips of one channel, newest first. Only a failed clip carries the red dot; a clip that worked
// reads as plain muted words. A clip written before the timing was kept has no synthesis time and shows a dash.
import { useI18n } from 'vue-i18n';

import type { ChannelTtsClip } from '@/api/adminDiagnostics';
import DateTime from '@/components/layout/DateTime.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';

import { synthesisText } from './ttsSynthesis';

defineProps<{ clips: readonly ChannelTtsClip[] }>();

const { t, locale } = useI18n();
</script>

<template>
  <Table stacked>
    <TableHeader>
      <TableRow class="hover:bg-transparent">
        <TableHead>{{ $t('dashboard.admin.console.tts.columns.time') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.tts.columns.provider') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.tts.columns.voice') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.console.tts.columns.characters') }}</TableHead>
        <TableHead class="text-right">{{ $t('dashboard.admin.console.tts.columns.synthesis') }}</TableHead>
        <TableHead>{{ $t('dashboard.admin.console.tts.columns.result') }}</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <TableRow v-for="(clip, index) in clips" :key="`${clip.occurredAt}-${index}`" data-tts-row>
        <TableCell stack="title" class="font-medium">
          <DateTime :value="clip.occurredAt" variant="dateTime" />
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.tts.columns.provider')">{{ clip.provider }}</TableCell>
        <TableCell class="font-mono text-sm wrap-anywhere" :label="$t('dashboard.admin.console.tts.columns.voice')">
          {{ clip.voiceId }}
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.console.tts.columns.characters')">
          {{ Number(clip.characterCount) }}
        </TableCell>
        <TableCell class="text-right tabular-nums" :label="$t('dashboard.admin.console.tts.columns.synthesis')">
          <span data-tts-synthesis>
            {{ synthesisText(clip.synthesisMs, locale, t('dashboard.admin.console.tts.none')) }}
          </span>
        </TableCell>
        <TableCell :label="$t('dashboard.admin.console.tts.columns.result')">
          <StatusBadge v-if="clip.failed" tone="bad" :label="t('dashboard.admin.console.tts.result.failed')" />
          <span v-else class="text-sm text-muted-foreground">{{ t('dashboard.admin.console.tts.result.ok') }}</span>
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</template>
