<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Says why a Discord picker has nothing to pick: the bot could not read the server's channels or roles.
import { useI18n } from 'vue-i18n';

import { errorText } from '@/lib/errors';

defineProps<{ error: Error | null }>();

const { t } = useI18n();
</script>

<template>
  <p v-if="error" role="alert" data-picker-error class="text-sm text-destructive">
    {{ t('dashboard.discord.editor.pickerError') }} {{ errorText(error, t) }}
  </p>
</template>
