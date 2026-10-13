<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The settings that stay in this browser. Today that is Readability only, so one link opens it.
import ArrowRight from '~icons/nnz/arrow-right';

import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

defineEmits<{ openReadability: [] }>();
</script>

<template>
  <PanelCard
    :title="$t('dashboard.settings.device.title')"
    :description="$t('dashboard.settings.device.description')"
    pad
  >
    <Button variant="ghost" size="sm" @click="$emit('openReadability')">
      <ArrowRight />
      {{ $t('dashboard.settings.device.openReadability') }}
    </Button>
  </PanelCard>
</template>
