#!/usr/bin/env python3
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
"""Field parity between the API, the old Compose dashboard and the Vue dashboard.

Part A: every channel write endpoint (POST/PUT/PATCH) in server/openapi/v1.json,
        each request body property, and whether a Vue view/component names it.
Part B: every Kotlin @Serializable request class the old app sends, grouped by
        the Compose feature screen that uses it, and whether Vue names each field.

A field counts as covered when its name appears as a whole word in a file under
dashboard/src/views or dashboard/src/components (tests excluded). This is a
word match: a lead for review, not proof of a working control.

Exit code 1 when any gap exists. Usage: check-ui-field-parity.py [--gaps-only]
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OPENAPI = ROOT / "server" / "openapi" / "v1.json"
VUE_SRC = ROOT / "dashboard" / "src"
NAV = VUE_SRC / "layouts" / "dashboard" / "navigation.ts"
KOTLIN = ROOT / "app" / "composeApp" / "src" / "commonMain" / "kotlin" / "bot" / "nomnomz" / "dashboard"
CHANNEL_PREFIX = "/api/v{version}/channels/"
REQUEST_NAME = re.compile(r"(Request|Body|Patch|Input|Payload|Command)$")


def fail(message: str) -> None:
    print(f"STOP: {message}")
    sys.exit(2)


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def source_files(base: Path, suffixes: tuple) -> list:
    files = []
    for path in base.rglob("*"):
        if path.suffix in suffixes and "__tests__" not in path.parts and ".spec." not in path.name:
            files.append(path)
    return files


def build_vue_corpus() -> str:
    parts = []
    for folder in ("views", "components"):
        for path in source_files(VUE_SRC / folder, (".vue", ".ts")):
            parts.append(read(path))
    return "\n".join(parts)


def word_in(corpus_words: set, name: str) -> bool:
    return name in corpus_words


def normalize_path(path: str) -> str:
    path = re.sub(r"\$\{[^}]*\}", "{}", path)
    return re.sub(r"\{[^}/]*\}", "{}", path).rstrip("/")


def vue_api_calls() -> set:
    calls = set()
    pattern = re.compile(r"client\s*\.\s*(POST|PUT|PATCH)\s*(?:<[^>]*>)?\(\s*[`'\"]([^`'\"]+)[`'\"]")
    for path in (VUE_SRC / "api").glob("*.ts"):
        for method, route in pattern.findall(read(path)):
            calls.add((method.lower(), normalize_path(route)))
    return calls


def resolve(schema: dict, components: dict, depth: int = 0) -> dict:
    if depth > 6 or not isinstance(schema, dict):
        return {}
    if "$ref" in schema:
        name = schema["$ref"].rsplit("/", 1)[-1]
        return resolve(components.get(name, {}), components, depth + 1)
    props = dict(schema.get("properties", {}))
    for part in schema.get("allOf", []):
        props.update(resolve(part, components, depth + 1))
    return props


def body_properties(operation: dict, components: dict) -> list:
    content = (operation.get("requestBody") or {}).get("content", {})
    for media in ("application/json", "text/json", "application/*+json"):
        if media in content:
            return sorted(resolve(content[media].get("schema", {}), components))
    return []


def nav_groups() -> dict:
    """Map a normalized page word (route name without 'dashboard-') to its sidebar group."""
    mapping = {}
    group = None
    for line in read(NAV).splitlines():
        key = re.match(r"\s{4}key: '(\w+)'", line)
        if key:
            group = key.group(1)
        route = re.search(r"route: 'dashboard-([\w-]+)'", line)
        if route and group:
            mapping[route.group(1).replace("-", "")] = group
    return mapping


def group_for(segment: str, nav: dict) -> str:
    word = segment.replace("-", "").lower()
    for page, group in nav.items():
        if page == word or page.rstrip("s") == word.rstrip("s"):
            return f"{group}/{segment}"
    for page, group in nav.items():
        if word and (word in page or page in word) and len(word) > 3:
            return f"{group}/{segment}"
    return f"other/{segment}"


def part_a(spec: dict, words: set, nav: dict) -> list:
    components = spec.get("components", {}).get("schemas", {})
    calls = vue_api_calls()
    rows = []
    for path, item in spec["paths"].items():
        if not path.startswith(CHANNEL_PREFIX):
            continue
        rest = path[len(CHANNEL_PREFIX):].split("/")
        segment = rest[1] if len(rest) > 1 else "channel"
        norm = normalize_path(path)
        for method in ("post", "put", "patch"):
            operation = item.get(method)
            if operation is None:
                continue
            called = (method, norm) in calls
            props = body_properties(operation, components)
            rows.append(
                {
                    "group": group_for(segment, nav),
                    "endpoint": f"{method.upper()} {path}",
                    "called": called,
                    "fields": [(p, word_in(words, p)) for p in props],
                }
            )
    return rows


def split_constructor(body: str) -> list:
    depth = 0
    current = ""
    parts = []
    for char in body:
        depth += char in "(<[{"
        depth -= char in ")>]}"
        if char == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char
    if current.strip():
        parts.append(current)
    return parts


def kotlin_request_classes() -> dict:
    classes = {}
    pattern = re.compile(r"@Serializable[^\n]*\n(?:@\w+[^\n]*\n)*\s*(?:internal\s+)?data class (\w+)\s*\(")
    for path in KOTLIN.rglob("*.kt"):
        text = read(path)
        for match in pattern.finditer(text):
            name = match.group(1)
            if not REQUEST_NAME.search(name):
                continue
            start = match.end()
            depth = 1
            index = start
            while index < len(text) and depth:
                depth += text[index] == "("
                depth -= text[index] == ")"
                index += 1
            fields = []
            for part in split_constructor(text[start : index - 1]):
                field = re.search(r"va[lr]\s+(\w+)\s*:", re.sub(r"/\*.*?\*/", "", part, flags=re.S))
                if field:
                    fields.append(field.group(1))
            classes[name] = fields
    return classes


def part_b(classes: dict, words: set, nav: dict) -> list:
    features = KOTLIN / "feature"
    feature_text = {}
    for folder in sorted(p for p in features.iterdir() if p.is_dir()):
        feature_text[folder.name] = "\n".join(read(f) for f in folder.rglob("*.kt"))
    rows = []
    for name, fields in sorted(classes.items()):
        pattern = re.compile(r"\b" + re.escape(name) + r"\b")
        for screen, text in feature_text.items():
            if pattern.search(text):
                rows.append(
                    {
                        "group": group_for(screen, nav),
                        "screen": screen,
                        "class": name,
                        "fields": [(f, word_in(words, f)) for f in fields],
                    }
                )
    return rows


def main() -> int:
    gaps_only = "--gaps-only" in sys.argv
    for needed in (OPENAPI, NAV, KOTLIN, VUE_SRC / "api"):
        if not needed.exists():
            fail(f"missing {needed}")
    spec = json.loads(read(OPENAPI))
    if "paths" not in spec:
        fail("v1.json has no paths")
    corpus = build_vue_corpus()
    words = set(re.findall(r"[A-Za-z_]\w*", corpus))
    nav = nav_groups()
    if not nav:
        fail("no routes found in navigation.ts")
    classes = kotlin_request_classes()
    if not classes:
        fail("no Kotlin request classes found")

    total = 0
    covered = 0
    endpoints_missing = 0
    rows_a = part_a(spec, words, nav)
    print("== PART A: channel write endpoints (OpenAPI) ==")
    print("group | endpoint | property | vue")
    for row in sorted(rows_a, key=lambda r: (r["group"], r["endpoint"])):
        if not row["called"]:
            endpoints_missing += 1
        if not row["fields"]:
            if not gaps_only or not row["called"]:
                print(f"{row['group']} | {row['endpoint']} | (no body) | api-call={'yes' if row['called'] else 'no'}")
            continue
        for prop, hit in row["fields"]:
            total += 1
            covered += hit
            if not gaps_only or not hit:
                print(f"{row['group']} | {row['endpoint']} | {prop} | vue={'yes' if hit else 'no'} | api-call={'yes' if row['called'] else 'no'}")

    rows_b = part_b(classes, words, nav)
    print("\n== PART B: old Compose request DTO fields ==")
    print("group | screen | class | field | vue")
    seen = set()
    for row in sorted(rows_b, key=lambda r: (r["group"], r["class"])):
        for field, hit in row["fields"]:
            total += 1
            covered += hit
            if not gaps_only or not hit:
                print(f"{row['group']} | {row['screen']} | {row['class']} | {field} | vue={'yes' if hit else 'no'}")
            seen.add((row["class"], field))

    print(f"\nEndpoints with no Vue api call: {endpoints_missing} of {len(rows_a)}")
    print(f"{covered} of {total} fields covered")
    return 0 if (covered == total and endpoints_missing == 0) else 1


if __name__ == "__main__":
    sys.exit(main())
