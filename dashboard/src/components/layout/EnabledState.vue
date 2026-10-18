<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
