<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The empty line of a channel picker: "Searching" while the server is asked, "No channel found" once it answered.
import { useI18n } from 'vue-i18n';

import { CommandEmpty } from '@/components/ui/command';

import { useChannelSearch } from './useChannelSearch';

const { t } = useI18n();
const { pending } = useChannelSearch();
</script>

<template>
  <CommandEmpty role="status">
    {{ pending ? t('frame.channel.searching') : t('frame.channel.noMatch') }}
  </CommandEmpty>
</template>
