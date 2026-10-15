<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one empty page for every page that is planned and not built yet: the sidebar shows it, the page says so.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import Hourglass from '~icons/nnz/hourglass';

import PageHeader from '@/components/layout/PageHeader.vue';
import {
  Empty,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';

const { t } = useI18n();
const route = useRoute();

const title = computed(() => t(`frame.nav.${route.meta.comingSoon ?? 'home'}`));
</script>

<template>
  <PageHeader :title="title" />
  <div class="flex min-h-0 flex-1 items-center justify-center overflow-y-auto p-6">
    <Empty>
      <EmptyHeader>
        <EmptyMedia variant="icon"><Hourglass aria-hidden="true" /></EmptyMedia>
        <EmptyTitle>{{ t('frame.comingSoon.title') }}</EmptyTitle>
        <EmptyDescription>{{ t('frame.comingSoon.description', { page: title }) }}</EmptyDescription>
      </EmptyHeader>
    </Empty>
  </div>
</template>
