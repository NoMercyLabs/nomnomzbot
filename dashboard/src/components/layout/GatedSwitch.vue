<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// An on/off setting: a Switch for a person who holds the permission `action`, an on/off badge for everyone
// else. It carries its own permission check, so a view never pairs a Switch with an EnabledState by hand.
// `platform` reads the platform admin keys instead of the channel keys.
// Other attributes (aria-labelledby, data-*, id) go to the Switch only; the badge has nothing to press.
import EnabledState from '@/components/layout/EnabledState.vue';
import { Switch } from '@/components/ui/switch';
import { useCan } from '@/composables/useCan';
import { usePlatformCan } from '@/composables/usePlatformCan';

defineOptions({ inheritAttrs: false });

const props = withDefaults(
  defineProps<{
    action: string;
    ariaLabel?: string;
    disabled?: boolean;
    platform?: boolean;
  }>(),
  {
    ariaLabel: undefined,
    disabled: false,
    platform: false,
  },
);

const checked = defineModel<boolean>({ default: false });
const { can } = props.platform
  ? usePlatformCan()
  : useCan();
</script>

<template>
  <Switch
    v-if="can(action)"
    v-bind="$attrs"
    v-model="checked"
    :disabled="disabled"
    :aria-label="ariaLabel"
  />
  <EnabledState v-else :enabled="checked" />
</template>
