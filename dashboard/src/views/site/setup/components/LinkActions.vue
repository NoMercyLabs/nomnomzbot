<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A web address the wizard asks the person to visit, with the two ways to use it: open it in a new tab, or
// copy it (into another browser profile, for example a second Twitch account). Both are secondary actions.
import ArrowUpRight from '~icons/nnz/arrow-up-right';

import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import { Button } from '@/components/ui/button';

defineProps<{ url: string }>();
</script>

<template>
  <span class="flex flex-wrap items-center gap-2">
    <code class="font-mono text-xs break-all">{{ url }}</code>
    <span class="flex items-center gap-2">
      <Button as-child variant="outline">
        <a :href="url" target="_blank" rel="noopener noreferrer">
          <ArrowUpRight class="size-5" aria-hidden="true" />
          {{ $t('site.setup.openLink') }}
        </a>
      </Button>
      <CopyLinkButton :url="url" :label="$t('site.setup.copyLink')" />
    </span>
  </span>
</template>
