# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------
#
# brief-check.sh: checks a draft agent brief against every dispatch guard BEFORE the Agent call,
# with this repo's rule registry. Exit 0 = PASS (dispatch the same text with the same type and
# model); exit 1 = the gaps, all at once; exit 2 = a usage error.
# Usage: sh scripts/brief-check.sh <draft.md> --type <subagent_type> --model <model>
exec node "C:/Projects/NoMercy/.claude/scripts/brief-check.mjs" "$@" --project "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
