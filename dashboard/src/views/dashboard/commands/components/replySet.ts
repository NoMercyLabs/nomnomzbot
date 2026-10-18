// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// One command's reply list that the random-reply dialog can copy from. It lives here, not in
// RandomReplyDialog.vue, so lint can read the type.
export interface ReplySet {
  name: string;
  replies: string[];
}
