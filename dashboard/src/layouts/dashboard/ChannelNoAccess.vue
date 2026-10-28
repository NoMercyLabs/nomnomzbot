<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import Locked from '~icons/nnz/locked';

import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty';

// A /app/u/<name>/... link to a channel the person cannot manage: said plainly, never a silent fallback.
defineProps<{ name: string }>();

const { t } = useI18n();
</script>

<template>
  <div
    data-slot="channel-no-access"
    class="flex min-h-0 flex-1 items-center justify-center overflow-y-auto p-6"
  >
    <Empty>
      <EmptyHeader>
        <EmptyMedia variant="icon"><Locked aria-hidden="true" /></EmptyMedia>
        <EmptyTitle>{{ t('frame.channel.noAccess.title', { name }) }}</EmptyTitle>
        <EmptyDescription>{{ t('frame.channel.noAccess.description') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>
  </div>
</template>
