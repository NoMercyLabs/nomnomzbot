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

import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

import PublicTaskShell from '../components/PublicTaskShell.vue';
import TaskActions from '../components/TaskActions.vue';

const { t } = useI18n();
</script>

<template>
  <PublicTaskShell :brand="t('site.notFound.brand')">
    <PanelCard
      :title="t('site.notFound.title')"
      :description="t('site.notFound.body')"
      heading="h1"
      tier="lead"
      pad
      class="w-full"
    >
      <TaskActions>
        <Button as-child>
          <RouterLink to="/">{{ t('site.notFound.home') }}</RouterLink>
        </Button>
      </TaskActions>
    </PanelCard>
  </PublicTaskShell>
</template>
