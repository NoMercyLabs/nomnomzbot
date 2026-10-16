<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import Eye from '~icons/nnz/eye';

import { useViewAs } from '@/api/viewAs';
import { Button } from '@/components/ui/button';

const { t } = useI18n();
const { role, exit } = useViewAs();
</script>

<template>
  <!-- Shown only while the owner previews a smaller role; Exit is the one way back to the full dashboard. -->
  <div
    v-if="role !== ''"
    data-slot="view-as-banner"
    role="status"
    class="flex flex-none items-center gap-2 border-b border-border bg-muted px-3 py-1.5 text-sm"
  >
    <Eye class="size-5 shrink-0 text-muted-foreground" aria-hidden="true" />
    <span class="min-w-0 flex-1 truncate">
      {{ t('frame.viewAs.banner', { role: t(`frame.viewAs.${role}`) }) }}
    </span>
    <Button variant="outline" size="sm" data-slot="view-as-exit" @click="exit">
      {{ t('frame.viewAs.exit') }}
    </Button>
  </div>
</template>
