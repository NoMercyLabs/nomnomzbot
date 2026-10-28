<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one empty page for every page that is planned and not built yet: the sidebar shows it, the page says so.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import Hourglass from '~icons/nnz/hourglass';

import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';

const { t, te } = useI18n();
const route = useRoute();

const title = computed(() => t(`frame.nav.${route.meta.comingSoon ?? 'home'}`));

// A page with a line of its own says what it will do; every other planned page keeps the plain line.
const description = computed(() => {
  const about = `frame.comingSoon.about.${route.meta.comingSoon}`;

  return te(about)
    ? t(about)
    : t('frame.comingSoon.description', { page: title.value });
});
</script>

<template>
  <PageHeader :title="title" />
  <PageBody class="items-center justify-center">
    <Empty>
      <EmptyHeader>
        <EmptyMedia variant="icon"><Hourglass aria-hidden="true" /></EmptyMedia>
        <EmptyTitle>{{ t('frame.comingSoon.title') }}</EmptyTitle>
        <EmptyDescription>{{ description }}</EmptyDescription>
      </EmptyHeader>
    </Empty>
  </PageBody>
</template>
