<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A step the bot lists but this page has no form for (a newer bot than this dashboard). It says so, never draws
// a wrong form, and the wizard keeps its own Next or Skip: this renderer holds no action.
import type { SetupStep } from '@/api/setup';

defineProps<{ step: SetupStep }>();
</script>

<template>
  <div data-slot="unknown-step" class="flex flex-col gap-1 text-sm">
    <p class="font-medium">{{ $t('site.setup.unknownStep.title') }}</p>
    <p class="text-muted-foreground">
      {{ step.required && !step.complete
        ? $t('site.setup.unknownStep.required')
        : $t('site.setup.unknownStep.optional') }}
    </p>
  </div>
</template>
