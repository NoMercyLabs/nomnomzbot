<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The state of an on/off setting for a person who may see it but not change it: a plain badge,
// never a switch that looks pressable and does nothing.
import { Badge } from '@/components/ui/badge';

defineProps<{ enabled: boolean }>();
</script>

<template>
  <Badge data-slot="enabled-state" :data-state="enabled ? 'on' : 'off'" variant="outline">
    {{ enabled ? $t('common.state.on') : $t('common.state.off') }}
  </Badge>
</template>
