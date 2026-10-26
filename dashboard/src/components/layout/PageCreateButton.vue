<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one create button of a page, for the PageHeader actions slot: the page's one primary action, so the
// default variant at size sm, with the Plus icon, on every page (owner 2026-10-26: create, edit and delete
// "have a stable spot in the ui, so they don't out of the blue appear on the other side").
// It is always rendered. When the person may not create, or an editor is open, a reason is passed and the
// button stays visible but disabled; a tooltip says why. Below the tablet width it moves into the frame's
// sticky action footer, like every page action (PageActionFooter).
import Plus from '~icons/nnz/add';
import { computed } from 'vue';

import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useScreenFrom } from '@/composables/useLayout';

defineOptions({ inheritAttrs: false });

const props = defineProps<{
  label: string;
  // Why creating is off right now; with no reason the button is on.
  disabledReason?: string;
}>();

defineEmits<{ create: [] }>();

const tablet = useScreenFrom('tablet');
const off = computed<boolean>(() => props.disabledReason !== undefined);
const buttonClass = computed<string>(() =>
  tablet.value
    ? ''
    : 'h-9! w-full');
</script>

<template>
  <PageActionFooter>
    <Tooltip :disabled="!off">
      <TooltipTrigger as-child>
        <!-- A disabled button gets no pointer or focus events, so the span carries the tooltip. -->
        <span
          data-slot="page-create"
          class="inline-flex"
          :class="tablet ? '' : 'w-full'"
          :tabindex="off ? 0 : undefined"
        >
          <Button
            v-bind="$attrs"
            type="button"
            variant="default"
            size="sm"
            :class="buttonClass"
            :disabled="off"
            @click="$emit('create')"
          >
            <Plus aria-hidden="true" />
            {{ label }}
          </Button>
        </span>
      </TooltipTrigger>
      <TooltipContent v-if="disabledReason">{{ disabledReason }}</TooltipContent>
    </Tooltip>
  </PageActionFooter>
</template>
