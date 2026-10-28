// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import type { FakeReply } from '../fakeApi';
import escalation from '../fixtures/moderation-escalation.json';
import spamPolicy from '../fixtures/spam-policy.json';
import { channelPath, fixture, get } from './shape';

// The spam-defense policy of a fresh install, as the server sends it: every setting at its default, the catalog of
// settings with their groups and bounds, and the invariants. Nothing here is edited for the sample.
// The escalation ladder is the server's default ladder and limits (EscalationPolicyDefaults.cs), switched on, which
// is the one thing the sample channel's owner would have changed.
export const moderationReplies: FakeReply[] = [
  get(`${channelPath}/spam-defense/policy`, fixture(spamPolicy)),
  get(`${channelPath}/moderation/escalation`, fixture(escalation)),
];
