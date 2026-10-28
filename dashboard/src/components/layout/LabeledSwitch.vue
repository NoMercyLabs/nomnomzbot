<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A switch with its label beside it, never across the panel. An editor's on/off sits in its header with
// the other actions, not as the first field of the form (owner 2026-10-17, as in the reward dialog).
// With `action`, the switch carries its own permission check (GatedSwitch): a person without it sees the badge.
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import { FieldInfo } from '@/components/ui/field';
import { Switch } from '@/components/ui/switch';

const on = defineModel<boolean>({ required: true });

const props = defineProps<{
  label: string;
  help?: string;
  disabled?: boolean;
  action?: string;
}>();
</script>

<template>
  <label data-labeled-switch class="flex w-fit items-center gap-2 text-sm font-medium">
    {{ label }}
    <FieldInfo v-if="help">{{ help }}</FieldInfo>
    <GatedSwitch
      v-if="action"
      v-model="on"
      :action="action"
      :aria-label="props.label"
      :disabled="disabled"
    />
    <Switch v-else v-model="on" :aria-label="props.label" :disabled="disabled" />
  </label>
</template>
