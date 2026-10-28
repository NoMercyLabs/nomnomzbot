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

import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

const { t } = useI18n();
</script>

<template>
  <main class="relative flex min-h-svh flex-col items-center justify-center gap-6 p-6">
    <div class="absolute top-4 right-4"><LanguagePicker /></div>
    <p class="flex items-center gap-2 text-base font-semibold">
      <AppMark />
      {{ t('site.notFound.brand') }}
    </p>
    <PanelCard
      :title="t('site.notFound.title')"
      :description="t('site.notFound.body')"
      tier="lead"
      pad
      class="w-full max-w-100"
    >
      <Button as-child class="w-full">
        <RouterLink to="/">{{ t('site.notFound.home') }}</RouterLink>
      </Button>
    </PanelCard>
  </main>
</template>
