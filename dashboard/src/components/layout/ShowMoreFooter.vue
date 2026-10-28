<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The foot of a capped list panel (the PanelCard footer slot): one ghost button that reveals the next rows.
import { Button } from '@/components/ui/button';

import ChevronDown from '~icons/nnz/chevron-down';

defineProps<{ remaining: number }>();
defineEmits<{ more: [] }>();
</script>

<template>
  <Button
    data-slot="show-more"
    type="button"
    variant="ghost"
    size="sm"
    class="w-full"
    @click="$emit('more')"
  >
    {{ $t("common.showMore", { count: remaining }) }}
    <ChevronDown aria-hidden="true" />
  </Button>
</template>
