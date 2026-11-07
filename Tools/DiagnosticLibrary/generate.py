#!/usr/bin/env python3
"""Generate PitMedic's public Diagnostic Library from the app knowledge base."""

from __future__ import annotations

import argparse
import html
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
WEBSITE = ROOT / "website"
OUTPUT = WEBSITE / "diagnostic-library"
BASE_URL = "https://pitmedic.com"
RELEASE_URL = "https://github.com/rholmes426/PitMedic/releases/download/v1.0.0.10/PitMedic-Setup-x64.exe"
RELEASE_VERSION_MATCH = re.search(r"/v([^/]+)/PitMedic-Setup-x64\.exe$", RELEASE_URL)
if not RELEASE_VERSION_MATCH:
    raise RuntimeError("Diagnostic Library release URL does not contain a valid PitMedic version")
RELEASE_VERSION = RELEASE_VERSION_MATCH.group(1)
LOGO_URL = "https://pitmedic.com/assets/brand/pitmedic-icon-carbon-lime.png"
# Date of the last editorial change to the collection page itself. Individual
# guide dates also advance its sitemap entry when the collection changes.
INDEX_MODIFIED = "2026-11-06"

GAME_SLUGS = {
    "Le Mans Ultimate": "le-mans-ultimate",
    "iRacing": "iracing",
    "Assetto Corsa EVO": "assetto-corsa-evo",
    "RaceRoom Racing Experience": "raceroom",
    "Assetto Corsa Competizione": "assetto-corsa-competizione",
    "Automobilista 2": "automobilista-2",
}

COMPANION_NAMES = {
    "MozaPitHouse": "MOZA Pit House",
    "SimucubeTrueDrive": "Simucube True Drive",
    "FanatecSoftware": "Fanatec software",
    "LogitechGHub": "Logitech G HUB",
    "SimagicSimProManager": "SIMAGIC SimPro Manager",
    "AsetekRaceHub": "Asetek RaceHub",
    "VrsDirectForce": "VRS DirectForce",
}

# Search-focused editorial content supplements the app-owned diagnostic record.
# Detection and repair behavior remains authoritative in the C# knowledge base;
# this layer explains priority issues in the language people use to seek help.
PUBLIC_CONTENT = json.loads((Path(__file__).with_name("guides.json")).read_text(encoding="utf-8"))


def csharp_string(value: str) -> str:
    return value.replace(r"\"", '"').replace(r"\\", "\\")


def field(block: str, name: str) -> str:
    match = re.search(rf'{name}\s*=\s*"((?:[^"\\]|\\.)*)"', block)
    if not match:
        raise ValueError(f"Missing {name} in knowledge entry")
    return csharp_string(match.group(1))


def references(block: str, helper_names: str = "Ref") -> list[dict[str, object]]:
    pattern = re.compile(
        rf'(?:{helper_names})\("((?:[^"\\]|\\.)*)",\s*'
        r'"((?:[^"\\]|\\.)*)",\s*'
        r'"((?:[^"\\]|\\.)*)",\s*'
        r'"((?:[^"\\]|\\.)*)"(?:,\s*(false|true))?\)',
        re.S,
    )
    result = []
    for title, source, url, note, official in pattern.findall(block):
        result.append({
            "title": csharp_string(title),
            "source": csharp_string(source),
            "url": csharp_string(url),
            "note": csharp_string(note),
            "official": official != "false" and helper_names != "Community",
        })
    return result


def parse_simulator_entries() -> list[dict[str, object]]:
    source = (ROOT / "Source/PitMedic/Services/RepairKnowledgeBase.cs").read_text(encoding="utf-8")
    blocks = re.findall(r"^        new KnowledgeEntry\s*\{(.*?)^        \},", source, re.M | re.S)
    entries = []
    for block in blocks:
        signatures_match = re.search(r"Signatures\s*=\s*new\[\]\s*\{(.*?)\}", block, re.S)
        signatures = re.findall(r'"((?:[^"\\]|\\.)*)"', signatures_match.group(1)) if signatures_match else []
        entries.append({
            "id": field(block, "Id"),
            "displayKind": "Diagnostic guidance" if "IsGuidanceOnly = true" in block else "Simulator repair",
            "product": field(block, "Game"),
            "issue": field(block, "Issue"),
            "detection": field(block, "Detection"),
            "repair": field(block, "RepairStrategy"),
            "safety": field(block, "Safety"),
            "signatures": [csharp_string(item) for item in signatures],
            "references": references(block),
        })
    return entries


def parse_companion_references() -> dict[str, list[dict[str, object]]]:
    source = (ROOT / "Source/PitMedic/Services/CompanionSoftwareKnowledgeBase.cs").read_text(encoding="utf-8")
    starts = list(re.finditer(r"^        CompanionSoftwareKind\.(\w+)\s*=>\s*new\[\]\s*$", source, re.M))
    result: dict[str, list[dict[str, object]]] = {}
    for index, match in enumerate(starts):
        end = starts[index + 1].start() if index + 1 < len(starts) else source.index("        _ =>", match.end())
        block = source[match.end():end]
        refs = references(block, "Official|Community")
        for ref, helper in zip(refs, re.findall(r"\b(Official|Community)\(", block)):
            ref["official"] = helper == "Official"
        result[match.group(1)] = refs
    return result


def parse_companion_entries() -> list[dict[str, object]]:
    source = (ROOT / "Source/PitMedic/Services/CompanionRecoveryPolicy.cs").read_text(encoding="utf-8")
    starts = list(re.finditer(r"^        new CompanionRecoveryDefinition\(", source, re.M))
    blocks = []
    for index, match in enumerate(starts):
        end = starts[index + 1].start() if index + 1 < len(starts) else source.index("    };", match.end())
        blocks.append(source[match.end():end])
    reference_map = parse_companion_references()
    entries = []
    for block in blocks:
        kind_match = re.search(r"CompanionSoftwareKind\.(\w+)", block)
        strings = [csharp_string(value) for value in re.findall(r'"((?:[^"\\]|\\.)*)"', block)]
        if not kind_match or len(strings) < 4:
            raise ValueError("Unable to parse companion recovery definition")
        kind = kind_match.group(1)
        repair_id, title, coverage, summary, *remaining = strings
        service_match = re.search(r'WindowsServiceName:\s*"((?:[^"\\]|\\.)*)"', block)
        service_name = csharp_string(service_match.group(1)) if service_match else ""
        steps = [item for item in remaining if item != service_name]
        entries.append({
            "id": repair_id,
            "displayKind": "Companion software repair",
            "product": COMPANION_NAMES[kind],
            "issue": title,
            "detection": coverage,
            "repair": summary,
            "safety": "Ask first / requires elevation" if "RequiresElevation: true" in block else "Ask first / controlled restart",
            "signatures": [],
            "steps": steps,
            "references": reference_map.get(kind, []),
        })
    return entries


def load_entries() -> list[dict[str, object]]:
    lifecycle = json.loads((ROOT / "Knowledge/lifecycle.json").read_text(encoding="utf-8"))
    lifecycle_by_id = {item["id"]: item for item in lifecycle["entries"]}
    entries = parse_simulator_entries() + parse_companion_entries()
    for entry in entries:
        if entry["id"] not in lifecycle_by_id:
            raise ValueError(f"{entry['id']} has no lifecycle record")
        entry.update(lifecycle_by_id[entry["id"]])
    known_ids = {entry["id"] for entry in entries}
    lifecycle_ids = set(lifecycle_by_id)
    if known_ids != lifecycle_ids:
        missing = sorted(lifecycle_ids - known_ids)
        raise ValueError(f"Lifecycle entries missing from generated library: {missing}")
    unknown_public_ids = sorted(set(PUBLIC_CONTENT) - known_ids)
    if unknown_public_ids:
        raise ValueError(f"Public content references unknown diagnostic records: {unknown_public_ids}")
    if set(PUBLIC_CONTENT) != known_ids:
        raise ValueError("Every diagnostic record must have a practical public guide")
    for entry_id, content in PUBLIC_CONTENT.items():
        missing_fields = sorted({"modified", "intro", "meaning", "checks", "result", "limits"} - set(content))
        if missing_fields:
            raise ValueError(f"{entry_id} public content is missing fields: {missing_fields}")
        if not isinstance(content["checks"], list) or len(content["checks"]) < 2:
            raise ValueError(f"{entry_id} needs at least two practical checks")
        if content.get("manual_steps"):
            if not content.get("manual_intro") or not content.get("manual_sources"):
                raise ValueError(f"{entry_id} manual steps need context and official sources")
            for source in content["manual_sources"]:
                if not source.get("title") or not source.get("url", "").startswith("https://"):
                    raise ValueError(f"{entry_id} manual source needs a title and HTTPS URL")
        for example in content.get("examples", []):
            if not {"title", "setup", "outcome", "scope", "source", "validation", "verified"} <= set(example):
                raise ValueError(f"{entry_id} example is missing its evidence or limits")
            if not re.fullmatch(r"https://github\.com/rholmes426/PitMedic/blob/[0-9a-f]{40}/Source/PitMedic\.ReleaseTests/[A-Za-z]+\.cs", example["source"]):
                raise ValueError(f"{entry_id} example needs an immutable test source")
            if not re.fullmatch(r"https://github\.com/rholmes426/PitMedic/actions/runs/[0-9]+/job/[0-9]+", example["validation"]):
                raise ValueError(f"{entry_id} example needs a validation job")
        unknown_related = sorted(set(content.get("related", [])) - known_ids)
        if unknown_related:
            raise ValueError(f"{entry_id} public content has unknown related records: {unknown_related}")
    return entries


def esc(value: object) -> str:
    return html.escape(str(value), quote=True)


def page_header(title: str, description: str, canonical: str, structured_data: dict[str, object]) -> str:
    return f'''<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{esc(title)}</title>
  <meta name="description" content="{esc(description)}" />
  <meta name="robots" content="index,follow" />
  <link rel="canonical" href="{esc(canonical)}" />
  <meta property="og:type" content="article" />
  <meta property="og:site_name" content="PitMedic" />
  <meta property="og:title" content="{esc(title)}" />
  <meta property="og:description" content="{esc(description)}" />
  <meta property="og:url" content="{esc(canonical)}" />
  <meta property="og:image" content="{LOGO_URL}" />
  <link rel="icon" href="/assets/brand/pitmedic-carbon-lime.ico" sizes="any" />
  <link rel="apple-touch-icon" href="/assets/brand/apple-touch-icon-carbon-lime.png" />
  <link rel="stylesheet" href="/styles.css" />
  <script type="application/ld+json">{json.dumps(structured_data, separators=(",", ":"), ensure_ascii=False).replace("</", "<\\/")}</script>
</head>
<body>
<main>
  <header class="site-header">
    <a class="brand" href="/" aria-label="PitMedic home"><img src="{LOGO_URL}" alt="" /><span>Pit<em>Medic</em></span></a>
    <nav aria-label="Primary navigation"><a href="/#how-it-works">How it works</a><a href="/#simulators">Simulators</a><a class="active" href="/diagnostic-library/">Diagnostic Library</a><a href="/#about">About</a></nav>
    <div class="header-actions"><a class="header-support" href="https://paypal.me/PitMedicApp" target="_blank" rel="noreferrer">Support PitMedic</a><a class="header-cta" href="/">Home</a></div>
  </header>'''


def page_footer() -> str:
    return f'''  <footer>
    <a class="brand" href="/"><img src="{LOGO_URL}" alt="" /><span>Pit<em>Medic</em></span></a>
    <p>A free open-source project for the sim-racing community.</p>
    <div><a href="/diagnostic-library/">Diagnostic Library</a><a href="/#simulators">Simulators</a><a href="https://github.com/rholmes426/PitMedic/blob/main/Source/PRIVACY.md">Privacy</a><a href="https://github.com/rholmes426/PitMedic">GitHub</a><a href="https://paypal.me/PitMedicApp">Support</a></div>
  </footer>
</main>
<script defer src="/analytics.js"></script>
<script type="module" src="https://static.cloudflareinsights.com/beacon.min.js" data-cf-beacon='{{"token":"5a4e361a3e0d4f838478d54e24d8f925"}}'></script>
</body>
</html>
'''


def repair_label(safety: str) -> str:
    lower = safety.lower()
    if "guidance only" in lower or "diagnostic" in lower or "community-derived" in lower:
        return "Guided diagnosis"
    if "ask" in lower or "significant" in lower or "approval required" in lower:
        return "Approval required"
    if "automatic" in lower or "one-click" in lower or "reversible" in lower:
        return "Automatic repair available"
    return "Approval required"


def breadcrumbs(items: list[tuple[str, str | None]]) -> tuple[str, dict[str, object]]:
    links = []
    schema_items = []
    for position, (name, url) in enumerate(items, 1):
        if url:
            links.append(f'<a href="{esc(url)}">{esc(name)}</a>')
            absolute = url if url.startswith("http") else BASE_URL + url
        else:
            links.append(f'<span>{esc(name)}</span>')
            absolute = None
        schema_item: dict[str, object] = {"@type": "ListItem", "position": position, "name": name}
        if absolute:
            schema_item["item"] = absolute
        schema_items.append(schema_item)
    return '<nav class="breadcrumbs" aria-label="Breadcrumb">' + '<span>›</span>'.join(links) + '</nav>', {
        "@type": "BreadcrumbList",
        "itemListElement": schema_items,
    }


def write_issue_page(entry: dict[str, object], all_entries: list[dict[str, object]], destination: Path) -> None:
    is_guidance = entry["status"] == "guidance"
    issue = str(entry["issue"])
    product = str(entry["product"])
    is_companion = entry["displayKind"] == "Companion software repair"
    display_issue = f"{product} crash or stale-process recovery" if is_companion else issue
    canonical = f"{BASE_URL}/diagnostic-library/{entry['id']}/"
    editorial = PUBLIC_CONTENT.get(str(entry["id"]), {})
    display_issue = str(editorial.get("heading") or display_issue)
    description = str(editorial.get("description") or (
        f"How PitMedic detects and performs a controlled recovery after a {product} crash or stale process."
        if is_companion else f"How PitMedic detects and safely responds to {issue.lower()} in {product}."
    ))
    crumb_html, crumb_schema = breadcrumbs([
        ("Home", "/"),
        ("Diagnostic Library", "/diagnostic-library/"),
        (product, None),
    ])
    schema = {
        "@context": "https://schema.org",
        "@graph": [
            crumb_schema,
            {
                "@type": "TechArticle",
                "headline": f"{product}: {display_issue}",
                "description": description,
                "url": canonical,
                "dateModified": max(str(editorial.get("modified", entry["lastVerified"])), str(entry["lastVerified"])),
                "author": {"@type": "Organization", "name": "PitMedic Project"},
                "about": product,
            },
        ],
    }
    source_items = "".join(
        f'''<li><div><strong>{esc(ref["title"])}</strong><span>{esc(ref["source"])} · {"Official source" if ref["official"] else "Community evidence"}</span><p>{esc(ref["note"])}</p></div><a href="{esc(ref["url"])}" target="_blank" rel="noreferrer">Open source</a></li>'''
        for ref in entry["references"]
    ) or '<li><div><strong>PitMedic recovery policy</strong><span>First-party implementation record</span><p>This recovery is documented by the behavior built into PitMedic.</p></div></li>'
    signals = "".join(f"<li><code>{esc(item)}</code></li>" for item in entry.get("signatures", []))
    steps = "".join(f"<li>{esc(item)}</li>" for item in entry.get("steps", []))
    details = f'<h3>Signals PitMedic may recognize</h3><ul class="signal-list">{signals}</ul>' if signals else ""
    if steps:
        details += f'<h3>Controlled recovery sequence</h3><ol class="repair-steps">{steps}</ol>'
    entry_by_id = {str(item["id"]): item for item in all_entries}
    editorial_related = [entry_by_id[item_id] for item_id in editorial.get("related", []) if item_id in entry_by_id]
    related = editorial_related or [item for item in all_entries if item["id"] != entry["id"] and (
        item["displayKind"] == entry["displayKind"] if is_companion else item["product"] == product
    )][:4]
    related_html = "".join(
        f'<a href="/diagnostic-library/{esc(item["id"])}/"><strong>{esc(item["issue"])}</strong><span>{esc(repair_label(str(item["safety"])))}</span></a>'
        for item in related
    )
    sim_slug = GAME_SLUGS.get(product)
    product_link = f'<a class="button button-secondary" href="/simulators/{sim_slug}/">View {esc(product)} coverage</a>' if sim_slug else '<a class="button button-secondary" href="/diagnostic-library/">Browse all diagnostics</a>'
    intro = str(editorial.get("intro") or "PitMedic contains a dedicated diagnostic record for this problem, including the evidence required before it recommends a repair.")
    meaning = str(editorial.get("meaning") or "")
    checks = editorial.get("checks", [])
    result = str(editorial.get("result") or "")
    explanation_html = ""
    if meaning or checks or result:
        checks_html = "".join(f"<li>{esc(item)}</li>" for item in checks)
        meaning_html = f"<p>{esc(meaning)}</p>" if meaning != intro else ""
        explanation_html = f'''        <section class="diagnostic-card diagnostic-guide"><h2>Checks and expected results</h2>{meaning_html}
          <h3>Before you repair it</h3><ul class="guide-checks">{checks_html}</ul>
          <h3>How to confirm the result</h3><p>{esc(result)}</p>
          <h3>Repair limits and when to stop</h3><p>{esc(editorial.get("limits", ""))}</p>
        </section>
'''
    examples_html = ""
    manual_html = ""
    if editorial.get("manual_steps"):
        manual_steps = "".join(f"<li>{esc(item)}</li>" for item in editorial["manual_steps"])
        manual_sources = "".join(
            f'<li><a href="{esc(source["url"])}">{esc(source["title"])}</a></li>'
            for source in editorial["manual_sources"]
        )
        manual_html = f'''<section class="diagnostic-card diagnostic-guide">
          <h2>Manual steps without PitMedic</h2>
          <p>{esc(editorial["manual_intro"])}</p>
          <ol class="guide-checks">{manual_steps}</ol>
          <h3>Official instructions</h3><ul>{manual_sources}</ul>
        </section>'''
    for example in editorial.get("examples", []):
        examples_html += f'''<section class="diagnostic-card diagnostic-example">
          <h2>{esc(example["title"])}</h2>
          <p class="example-label">Verified automated regression test · {esc(example["verified"])}</p>
          <h3>Test setup</h3><p>{esc(example["setup"])}</p>
          <h3>Observed result</h3><p>{esc(example["outcome"])}</p>
          <h3>What this establishes</h3><p>{esc(example["scope"])}</p>
          <p class="example-evidence"><a href="{esc(example["source"])}">Read the test source</a> · <a href="{esc(example["validation"])}">View the passing Windows test run</a></p>
        </section>'''
    body = f'''
  <article class="library-page issue-page">
    {crumb_html}
    <header class="library-hero">
      <span class="section-kicker">{esc(entry["displayKind"])}</span>
      <p class="library-product">{esc(product)}</p>
      <h1>{esc(display_issue)}</h1>
      <p>{esc(intro)}</p>
      <div class="issue-status"><span>{esc(repair_label(str(entry["safety"])))}</span><span>{"Guidance" if is_guidance else "Active"}</span><span>Evidence reviewed {esc(entry["lastVerified"])}</span><span>Guide updated {esc(editorial["modified"])}</span></div>
    </header>
    <div class="diagnostic-layout">
      <div class="diagnostic-main">
{explanation_html}{manual_html}        <section class="diagnostic-card"><h2>{"When to consider this guidance" if is_guidance else "How PitMedic recognizes it"}</h2><p>{esc(entry["detection"])}</p>{details}</section>
        <section class="diagnostic-card"><h2>{"Suggested manual checks" if is_guidance else "Built-in response"}</h2><p>{esc(entry["repair"])}</p><div class="safety-note"><strong>Repair safety</strong><span>{esc(entry["safety"])}</span></div></section>
{examples_html}        <section class="diagnostic-card"><h2>Verification sources</h2><p>PitMedic prioritizes vendor documentation and labels community findings separately.</p><ul class="source-list">{source_items}</ul></section>
      </div>
      <aside class="library-aside"><h2>{"Compare your symptoms" if is_guidance else "Let PitMedic check it"}</h2><p>{"This is version-specific guidance for manual review. It does not add automatic detection or a repair action." if is_guidance else "PitMedic compares the evidence on your PC with this record. It only offers a repair when the relevant conditions are present."}</p><a class="button button-primary" href="{RELEASE_URL}">Download v{RELEASE_VERSION}</a>{product_link}<small>Free and open source · Windows 10/11</small></aside>
    </div>
    <nav class="related-diagnostics" aria-label="Related diagnostics"><h2>{"Other companion software recoveries" if is_companion else "More for " + esc(product)}</h2><div>{related_html}</div></nav>
  </article>
'''
    destination.mkdir(parents=True, exist_ok=True)
    page_title = str(editorial.get("title") or (f"{product} Crash & Stale Process Recovery | PitMedic" if is_companion else f"{issue} — {product} | PitMedic"))
    (destination / "index.html").write_text(page_header(page_title, description, canonical, schema) + body + page_footer(), encoding="utf-8")



# Stable section anchors preserve guide URLs and avoid duplicate filter landing pages.
LIBRARY_GROUPS = [
    {
        "slug": "iracing",
        "name": "iRacing",
        "products": [
            "iRacing"
        ],
        "description": "Loading errors, failed updates, anti-cheat messages, frozen UI, and renderer settings.",
        "coverage": "iracing"
    },
    {
        "slug": "le-mans-ultimate",
        "name": "Le Mans Ultimate",
        "products": [
            "Le Mans Ultimate"
        ],
        "description": "Startup crashes, track loading failures, plugins, overlays, memory, and online sessions.",
        "coverage": "le-mans-ultimate"
    },
    {
        "slug": "assetto-corsa-competizione",
        "name": "Assetto Corsa Competizione (ACC)",
        "products": [
            "Assetto Corsa Competizione"
        ],
        "description": "Display settings, Controls menu crashes, wheel input, force feedback, and damaged game files.",
        "coverage": "assetto-corsa-competizione"
    },
    {
        "slug": "automobilista-2",
        "name": "Automobilista 2 (AMS2)",
        "products": [
            "Automobilista 2"
        ],
        "description": "Graphics and VR configuration, controller calibration, custom force feedback, setups, and profiles.",
        "coverage": "automobilista-2"
    },
    {
        "slug": "raceroom",
        "name": "RaceRoom",
        "products": [
            "RaceRoom Racing Experience"
        ],
        "description": "Error 503, blank UI, graphics settings, shader cache, and damaged configuration.",
        "coverage": "raceroom"
    },
    {
        "slug": "assetto-corsa-evo",
        "name": "Assetto Corsa EVO",
        "products": [
            "Assetto Corsa EVO"
        ],
        "description": "Video settings, launch failures, user profiles, Steam files, and companion conflicts.",
        "coverage": "assetto-corsa-evo"
    },
    {
        "slug": "companion-software",
        "name": "Wheelbase and companion software",
        "products": [
            "MOZA Pit House",
            "Simucube True Drive",
            "Simucube Tuner",
            "Fanatec software",
            "Logitech G HUB",
            "SIMAGIC SimPro Manager",
            "Asetek RaceHub",
            "VRS DirectForce"
        ],
        "description": "MOZA, Simucube, Fanatec, Logitech, SIMAGIC, Asetek, and VRS app crashes, stale processes, and version-specific guidance.",
        "coverage": ""
    }
]
def library_title(entry: dict[str, object]) -> str:
    heading = str(PUBLIC_CONTENT[str(entry["id"])].get("heading") or entry["issue"])
    product = str(entry["product"])
    return heading if product.lower() in heading.lower() else f"{product}: {heading}"


def write_index(entries: list[dict[str, object]], destination: Path) -> None:
    canonical = f"{BASE_URL}/diagnostic-library/"
    description = "Find sim racing troubleshooting guides for loading errors, crashes, updates, wheel input, and force feedback. Browse by simulator or companion software."
    crumb_html, crumb_schema = breadcrumbs([("Home", "/"), ("Diagnostic Library", None)])
    grouped = [(group, [entry for entry in entries if entry["product"] in group["products"]]) for group in LIBRARY_GROUPS]
    ordered = [entry for _, members in grouped for entry in members]
    if len(ordered) != len(entries) or len({entry["id"] for entry in ordered}) != len(entries):
        raise ValueError("Every diagnostic must appear in exactly one library section")
    sections = []
    for group, members in grouped:
        rows = []
        for entry in members:
            heading = str(PUBLIC_CONTENT[str(entry["id"])].get("heading") or entry["issue"])
            # The group heading already identifies the simulator. Retain companion
            # product names because several different apps share that section.
            if group["coverage"]:
                heading = re.sub(rf"^{re.escape(str(entry['product']))}\s*:?\s+", "", heading, flags=re.I)
                heading = heading[:1].upper() + heading[1:]
            label = repair_label(str(entry["safety"]))
            short_label = {"Automatic repair available": "Automatic repair", "Approval required": "Repair with approval", "Guided diagnosis": "Guided checks"}[label]
            search = " ".join([str(entry["product"]), str(group["name"]), str(entry["issue"]), library_title(entry), str(entry["detection"]), *entry.get("signatures", [])]).lower()
            rows.append(f'''
          <li class="directory-row" data-product="{esc(str(entry["product"]).lower())}" data-repair="{esc(label.lower())}" data-search="{esc(search)}">
            <a href="/diagnostic-library/{esc(entry['id'])}/"><span class="directory-row-title">{esc(heading)}</span><span class="directory-row-kind">{esc(short_label)}</span><span class="directory-arrow" aria-hidden="true">↗</span></a>
          </li>''')
        name = "Companion software" if not group["coverage"] else str(group["name"])
        sections.append(f'''
      <details class="directory-group" id="{esc(group['slug'])}" name="library-software">
        <summary><span class="directory-group-name">{esc(name)}</span><span class="directory-group-count">{len(members)} guides</span><span class="directory-chevron" aria-hidden="true"></span></summary>
        <ul class="directory-list">{"".join(rows)}
        </ul>
      </details>''')
    schema = {
        "@context": "https://schema.org",
        "@graph": [crumb_schema, {
            "@type": "CollectionPage", "name": "PitMedic Diagnostic Library",
            "description": description, "url": canonical,
            "mainEntity": {
                "@type": "ItemList", "numberOfItems": len(ordered),
                "itemListElement": [
                    {"@type": "ListItem", "position": position, "name": library_title(entry), "url": f"{canonical}{entry['id']}/"}
                    for position, entry in enumerate(ordered, 1)
                ],
            },
        }],
    }
    body = f'''
  <section class="library-page library-directory">
    {crumb_html}
    <header class="directory-hero">
      <span class="section-kicker">Sim racing troubleshooting</span>
      <h1>Diagnostic Library</h1>
      <p>Choose your software to find fixes for errors, crashes, and setup problems.</p>
    </header>
    <form class="directory-search" role="search" onsubmit="return false">
      <label class="directory-search-label" for="library-search">Search {len(entries)} troubleshooting guides</label>
      <div class="directory-search-field"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10.5" cy="10.5" r="6.5"/><path d="m16 16 5 5"/></svg><input id="library-search" type="search" placeholder="Search an error message or symptom…" autocomplete="off" /><button id="library-reset" type="button" hidden>Clear</button></div>
      <p class="directory-results" id="library-results" role="status" aria-live="polite" hidden></p>
    </form>
    <div class="directory-heading"><h2>Browse by software</h2><span>{len(entries)} guides</span></div>
    <div class="directory-groups">{"".join(sections)}
    </div>
    <p class="directory-empty" id="library-empty" hidden>No guides found. Try a different error message or clear your search.</p>
    <p class="directory-note">Each guide explains what to check and which repairs are available.</p>
  </section>
  <script>
    (() => {{
      const search = document.querySelector('#library-search');
      const reset = document.querySelector('#library-reset');
      const results = document.querySelector('#library-results');
      const empty = document.querySelector('#library-empty');
      const groups = [...document.querySelectorAll('.directory-group')];
      const rows = [...document.querySelectorAll('.directory-row')];
      let requestedProduct = '';
      let requestedRepair = '';
      const filter = () => {{
        const terms = search.value.trim().toLowerCase().split(/\\s+/).filter(Boolean);
        const active = terms.length > 0 || !!requestedProduct || !!requestedRepair;
        rows.forEach(row => {{
          row.hidden = !terms.every(term => row.dataset.search.includes(term)) ||
            (!!requestedProduct && row.dataset.product !== requestedProduct) ||
            (!!requestedRepair && row.dataset.repair !== requestedRepair);
        }});
        // Native details groups share a name for normal browsing. Searching can
        // show matches in several sections at once.
        groups.forEach(group => {{
          const matching = [...group.querySelectorAll('.directory-row')].filter(row => !row.hidden).length;
          if (active) group.removeAttribute('name');
          group.hidden = active && matching === 0;
          group.open = active && matching > 0;
          group.querySelector('.directory-group-count').textContent = `${{matching}} ${{matching === 1 ? 'guide' : 'guides'}}`;
          if (!active) group.setAttribute('name', 'library-software');
        }});
        const count = rows.filter(row => !row.hidden).length;
        results.textContent = `${{count}} matching ${{count === 1 ? 'guide' : 'guides'}}`;
        results.hidden = !active;
        reset.hidden = !active;
        empty.hidden = count !== 0;
      }};
      const readLocation = () => {{
        const params = new URLSearchParams(location.search);
        search.value = params.get('q') || '';
        requestedProduct = (params.get('software') || '').replaceAll('-', ' ').toLowerCase();
        requestedRepair = params.get('coverage') || '';
        filter();
        if (!search.value && !requestedProduct && !requestedRepair) {{
          const target = groups.find(group => `#${{group.id}}` === location.hash);
          if (target) target.open = true;
        }}
      }};
      search.addEventListener('input', () => {{
        requestedProduct = '';
        requestedRepair = '';
        filter();
        const url = new URL(location.href);
        url.searchParams.delete('software');
        url.searchParams.delete('coverage');
        if (search.value.trim()) url.searchParams.set('q', search.value.trim());
        else url.searchParams.delete('q');
        url.hash = '';
        history.replaceState(null, '', url);
      }});
      reset.addEventListener('click', () => {{
        search.value = '';
        requestedProduct = '';
        requestedRepair = '';
        filter();
        const url = new URL(location.href);
        ['q', 'software', 'coverage'].forEach(key => url.searchParams.delete(key));
        url.hash = '';
        history.replaceState(null, '', url);
        search.focus();
      }});
      window.addEventListener('popstate', readLocation);
      window.addEventListener('hashchange', readLocation);
      readLocation();
    }})();
  </script>
'''
    destination.mkdir(parents=True, exist_ok=True)
    header = page_header("Sim Racing Troubleshooting Guides & Fixes | PitMedic", description, canonical, schema)
    # Fetch the directory styles together with its markup after deployment.
    header = header.replace('href="/styles.css"', 'href="/styles.css?library=compact-20261106"')
    (destination / "index.html").write_text(header + body + page_footer(), encoding="utf-8")


def write_public_json(entries: list[dict[str, object]], destination: Path) -> None:
    payload = {
        "schemaVersion": 1,
        "generatedFrom": [
            "Source/PitMedic/Services/RepairKnowledgeBase.cs",
            "Source/PitMedic/Services/CompanionRecoveryPolicy.cs",
            "Knowledge/lifecycle.json",
        ],
        "entries": entries,
    }
    (destination / "database.json").write_text(json.dumps(payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def write_sitemap(entries: list[dict[str, object]], path: Path) -> None:
    # A missing optional lastmod is better than an invented or frozen date.
    # These hand-maintained pages do not have a reliable editorial date source.
    collection_modified = max(INDEX_MODIFIED, *(
        max(str(PUBLIC_CONTENT[str(entry["id"])]["modified"]), str(entry["lastVerified"]))
        for entry in entries
    ))
    existing = [
        ("/", None, "weekly", "1.0"),
        ("/simulators/iracing/", None, "monthly", "0.8"),
        ("/simulators/le-mans-ultimate/", None, "monthly", "0.8"),
        ("/simulators/assetto-corsa-competizione/", None, "monthly", "0.8"),
        ("/simulators/automobilista-2/", None, "monthly", "0.8"),
        ("/simulators/raceroom/", None, "monthly", "0.8"),
        ("/simulators/assetto-corsa-evo/", None, "monthly", "0.8"),
        ("/diagnostic-library/", collection_modified, "weekly", "0.9"),
    ]
    urls = existing + [(
        f"/diagnostic-library/{entry['id']}/",
        max(str(PUBLIC_CONTENT.get(str(entry["id"]), {}).get("modified", entry["lastVerified"])), str(entry["lastVerified"])),
        "monthly",
        "0.7",
    ) for entry in entries]
    rows = "\n".join(
        f"  <url><loc>{BASE_URL}{url}</loc>"
        + (f"<lastmod>{date}</lastmod>" if date else "")
        + f"<changefreq>{frequency}</changefreq><priority>{priority}</priority></url>"
        for url, date, frequency, priority in urls
    )
    path.write_text(f'''<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
{rows}
</urlset>
''', encoding="utf-8")


def generate(destination: Path, sitemap: Path) -> None:
    entries = load_entries()
    if len({entry["id"] for entry in entries}) != len(entries):
        raise ValueError("Duplicate diagnostic record IDs")
    destination.mkdir(parents=True, exist_ok=True)
    write_index(entries, destination)
    write_public_json(entries, destination)
    for entry in entries:
        write_issue_page(entry, entries, destination / str(entry["id"]))
    write_sitemap(entries, sitemap)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="Fail if committed generated files are stale")
    args = parser.parse_args()
    if not args.check:
        if OUTPUT.exists():
            shutil.rmtree(OUTPUT)
        generate(OUTPUT, WEBSITE / "sitemap.xml")
        print(f"Generated {len(load_entries())} Diagnostic Library records.")
        return 0

    with tempfile.TemporaryDirectory() as temp:
        temp_root = Path(temp)
        generated = temp_root / "diagnostic-library"
        sitemap = temp_root / "sitemap.xml"
        generate(generated, sitemap)
        if not OUTPUT.exists() or list(generated.rglob("*")) == []:
            print("Diagnostic Library output is missing.", file=sys.stderr)
            return 1
        mismatches = []
        generated_files = {path.relative_to(generated) for path in generated.rglob("*") if path.is_file()}
        committed_files = {path.relative_to(OUTPUT) for path in OUTPUT.rglob("*") if path.is_file()}
        for relative in sorted(generated_files | committed_files):
            expected = generated / relative
            actual = OUTPUT / relative
            if not expected.exists() or not actual.exists() or expected.read_bytes() != actual.read_bytes():
                mismatches.append(str(relative))
        if sitemap.read_bytes() != (WEBSITE / "sitemap.xml").read_bytes():
            mismatches.append("../sitemap.xml")
        if mismatches:
            print("Generated Diagnostic Library files are stale: " + ", ".join(mismatches[:10]), file=sys.stderr)
            return 1
    print("Diagnostic Library generated files are current.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

