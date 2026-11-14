<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The frame of one settings page: the header (the title from the route's titleKey, the crumb, the
// page's header slots) and, by default, the approved body. A page that brings its own approved body
// recipe (a list with a detail, tabs) sets body="none" and draws it in the default slot.
// Slots: actions (header buttons), status and meta (header text), tabs and bar (the header bar: tabs for a
// page that switches views, bar for list filters), panel (a SidePanel beside the body) and the default body.
// A list with a detail is a body of its own: set body="none" and draw ListDetail in the default slot.
// title overrides the route's title for a page that computes it; crumb overrides the Settings crumb
// for a page that sits in a named group (Moderation, Account).
import { computed, inject } from 'vue';
import { useI18n } from 'vue-i18n';
import { routeLocationKey } from 'vue-router';

import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';

defineOptions({ inheritAttrs: false });

const props = defineProps<{
  title?: string;
  crumb?: string;
  body?: 'none';
  dense?: boolean;
}>();

const { t } = useI18n();

// A page mounted without a router (a unit test of one panel) has no route and so no title.
const route = inject(routeLocationKey, null);

const heading = computed(() => {
  const titleKey = route?.meta.titleKey;

  return props.title ??
    (titleKey
      ? t(titleKey)
      : '');
});
</script>

<template>
  <PageHeader :title="heading" :crumb="crumb ?? t('dashboard.settings.crumb')">
    <template v-if="$slots.status" #status><slot name="status" /></template>
    <template v-if="$slots.meta" #meta><slot name="meta" /></template>
    <template v-if="$slots.actions" #actions><slot name="actions" /></template>
    <template v-if="$slots.tabs || $slots.bar" #bar><slot name="tabs" /><slot name="bar" /></template>
  </PageHeader>

  <slot v-if="body === 'none'" />
  <PanelBeside v-else-if="$slots.panel">
    <PageBody v-bind="$attrs" :dense="dense"><slot /></PageBody>
    <template #panel><slot name="panel" /></template>
  </PanelBeside>
  <PageBody v-else v-bind="$attrs" :dense="dense"><slot /></PageBody>
</template>
