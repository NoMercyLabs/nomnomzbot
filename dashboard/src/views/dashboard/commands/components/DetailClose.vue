<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The close button of an open command's panel. A phone has the "Back to the list" link instead.
import Cross from '~icons/nnz/cross';

import IconAction from '@/components/layout/IconAction.vue';
</script>

<template>
  <IconAction data-detail-close class="max-md:hidden" :label="$t('common.close')">
    <Cross class="size-5" aria-hidden="true" />
  </IconAction>
</template>
