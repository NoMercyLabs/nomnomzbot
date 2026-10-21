<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every sign-in action on the home page. Sign-in is closed for now, so each one is the same disabled
// "Coming soon" button with its note; opening sign-in later changes this one file.
import { useI18n } from 'vue-i18n';

import ComingSoonButton from '../components/ComingSoonButton.vue';

type Props = InstanceType<typeof ComingSoonButton>['$props'];

defineProps<{
  variant?: Props['variant'];
  size?: Props['size'];
  showNote?: boolean;
}>();

const { t } = useI18n();
</script>

<template>
  <ComingSoonButton
    :variant="variant"
    :size="size"
    :show-note="showNote"
    :label="t('site.home.comingSoon')"
    :note="t('site.home.comingSoonNote')"
  />
</template>
