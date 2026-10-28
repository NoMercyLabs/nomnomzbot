<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channel's plan and what goes with it: the plans on offer, an invite code and the invoices. The limits
// and usage of the plan are on Resource limits. Each panel reads its own data and refreshes on the
// `billing` config change of the hub.
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';

import InvitePanel from './components/InvitePanel.vue';
import InvoicesPanel from './components/InvoicesPanel.vue';
import PlanPanel from './components/PlanPanel.vue';
import TiersPanel from './components/TiersPanel.vue';
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.billing')"
    :crumb="$t('dashboard.settings.billing.crumb')"
  />

  <PageBody>
    <div data-view="billing" class="grid gap-4">
      <PlanPanel />
      <TiersPanel />
      <InvitePanel />
      <InvoicesPanel />
    </div>
  </PageBody>
</template>
