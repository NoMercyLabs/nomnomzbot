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
import Translate from '~icons/nnz/translate';

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { chooseLocale, locales, ownLanguageName } from '@/i18n';

const { locale } = useI18n();
</script>

<template>
  <Select :model-value="locale" @update:model-value="chooseLocale">
    <SelectTrigger :aria-label="$t('common.language')">
      <Translate aria-hidden="true" />
      <SelectValue />
    </SelectTrigger>
    <!-- Below the button, so the open list never covers the picker or the header action beside it. -->
    <SelectContent position="popper" align="end">
      <!-- The group carries the list padding in this shadcn style; options straight in the list touch its edge. -->
      <SelectGroup>
        <SelectItem v-for="tag in locales" :key="tag" :value="tag" :lang="tag">{{
          ownLanguageName(tag)
        }}</SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>
</template>
