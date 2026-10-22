<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
