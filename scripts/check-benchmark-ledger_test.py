# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# Tests for scripts/check-benchmark-ledger.py.  Run: python scripts/check-benchmark-ledger_test.py

import importlib.util
import pathlib
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    'check_benchmark_ledger', pathlib.Path(__file__).with_name('check-benchmark-ledger.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)

PRODUCTS = (
    '## Products\n\n'
    '- VS Code https://code.visualstudio.com/docs\n'
    '- CodePen https://blog.codepen.io/documentation/\n'
    '- StackBlitz https://developer.stackblitz.com/\n\n'
)
HEADER = '| Feature | Seen in | Source | Ours | Status | Slice |\n|---|---|---|---|---|---|\n'
PLAN = '## OWNER REQUEST 2026-10-07 - editor like VS Code\nSee benchmarks/editor.md\n- **S-ED-ONE** one\n'


def have_rows(count):
    return ''.join(f'| feature {i} | VS Code | https://x/{i} | editor.js | have | |\n' for i in range(count))


def benchmark(rows, products=PRODUCTS):
    return '# Editor benchmark\n\n' + products + '## Features\n\n' + HEADER + rows


def run(plan_text, files):
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)
        bench = root / 'benchmarks'
        bench.mkdir()
        for name, text in files.items():
            (bench / name).write_text(text, encoding='utf-8')
        slices = gate.plan_slices(plan_text)
        problems = []
        for section in gate.owner_request_sections(plan_text):
            if gate.ASKS_RESEARCH.search(section) and not gate.BENCHMARK_LINK.findall(section):
                problems.append('no link')
        for path in sorted(bench.glob('*.md')):
            problems.extend(gate.check_benchmark(path, slices))
        return problems


class CheckBenchmarkLedgerTests(unittest.TestCase):
    def test_a_full_benchmark_linked_from_the_owner_request_passes(self):
        self.assertEqual([], run(PLAN, {'editor.md': benchmark(have_rows(25))}))

    def test_an_owner_request_that_asks_for_research_without_a_benchmark_fails(self):
        plan = '## OWNER REQUEST 2026-10-07 - do research on the editor\nread some UX books\n'
        self.assertEqual(['no link'], run(plan, {}))

    def test_a_reading_list_of_a_few_rows_is_not_a_benchmark(self):
        problems = run(PLAN, {'editor.md': benchmark(have_rows(5))})
        self.assertTrue(any('5 feature rows, need at least 25' in p for p in problems), problems)

    def test_fewer_than_three_products_or_one_without_a_url_fails(self):
        products = '## Products\n\n- VS Code https://code.visualstudio.com\n- CodePen\n\n'
        problems = run(PLAN, {'editor.md': benchmark(have_rows(25), products)})
        self.assertTrue(any('2 products' in p for p in problems), problems)
        self.assertTrue(any('without a source URL' in p for p in problems), problems)

    def test_a_missing_feature_must_name_a_slice_that_is_in_the_plan(self):
        rows = have_rows(24) + '| json tab | VS Code | https://x | | missing | S-ED-NOT-PLANNED |\n'
        problems = run(PLAN, {'editor.md': benchmark(rows)})
        self.assertTrue(any('S-ED-NOT-PLANNED is not in the plan' in p for p in problems), problems)

    def test_not_doing_needs_a_reason(self):
        rows = have_rows(24) + '| live share | VS Code | https://x | | not-doing | no |\n'
        problems = run(PLAN, {'editor.md': benchmark(rows)})
        self.assertTrue(any('not-doing needs a reason' in p for p in problems), problems)

    def test_an_unknown_status_fails(self):
        rows = have_rows(24) + '| json tab | VS Code | https://x | editor.js | done | |\n'
        problems = run(PLAN, {'editor.md': benchmark(rows)})
        self.assertTrue(any('status "done"' in p for p in problems), problems)


if __name__ == '__main__':
    unittest.main()
