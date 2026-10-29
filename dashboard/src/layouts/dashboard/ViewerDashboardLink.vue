<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The way back from the viewer pages to the dashboard, for a person who manages a channel (a streamer or a
// moderator). A viewer with no channel has no dashboard to go to, and a previewed role has the banner's Exit.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useViewAs } from '@/api/viewAs';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { t } = useI18n();
const { current } = useCurrentChannel();
const preview = useViewAs();

const visible = computed<boolean>(() => current.value !== undefined && preview.role.value === '');
</script>

<template>
  <Button v-if="visible" as-child variant="ghost" size="sm">
    <RouterLink to="/app">{{ t('frame.viewerDashboard') }}</RouterLink>
  </Button>
</template>
