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
RELEASE_URL = "https://github.com/rholmes426/PitMedic/releases/download/v1.0.0.8/PitMedic-Setup-x64.exe"
RELEASE_VERSION_MATCH = re.search(r"/v([^/]+)/PitMedic-Setup-x64\.exe$", RELEASE_URL)
if not RELEASE_VERSION_MATCH:
    raise RuntimeError("Diagnostic Library release URL does not contain a valid PitMedic version")
RELEASE_VERSION = RELEASE_VERSION_MATCH.group(1)
LOGO_URL = "https://pitmedic.com/assets/brand/pitmedic-icon-carbon-lime.png"
TODAY = "2026-09-03"

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
{explanation_html}        <section class="diagnostic-card"><h2>{"When to consider this guidance" if is_guidance else "How PitMedic recognizes it"}</h2><p>{esc(entry["detection"])}</p>{details}</section>
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


def write_index(entries: list[dict[str, object]], destination: Path) -> None:
    canonical = f"{BASE_URL}/diagnostic-library/"
    description = f"Browse {len(entries)} sim-racing troubleshooting guides for iRacing, Le Mans Ultimate, ACC, AMS2, RaceRoom, Assetto Corsa EVO, and companion software."
    crumb_html, crumb_schema = breadcrumbs([("Home", "/"), ("Diagnostic Library", None)])
    products = sorted({str(entry["product"]) for entry in entries})
    product_options = "".join(f'<option value="{esc(product.lower())}">{esc(product)}</option>' for product in products)
    cards = []
    for entry in entries:
        label = repair_label(str(entry["safety"]))
        search = " ".join([str(entry["product"]), str(entry["issue"]), str(entry["detection"]), *entry.get("signatures", [])]).lower()
        cards.append(f'''
        <article class="library-card" data-product="{esc(str(entry["product"]).lower())}" data-kind="{esc(str(entry["kind"]).lower())}" data-repair="{esc(label.lower())}" data-search="{esc(search)}">
          <span class="library-card-product">{esc(entry["product"])}</span>
          <h2><a href="/diagnostic-library/{esc(entry["id"])}/">{esc(entry["issue"])}</a></h2>
          <p>{esc(entry["detection"])}</p>
          <div><span>{esc(label)}</span><a href="/diagnostic-library/{esc(entry["id"])}/">View diagnostic →</a></div>
        </article>''')
    schema = {
        "@context": "https://schema.org",
        "@graph": [crumb_schema, {
            "@type": "CollectionPage",
            "name": "PitMedic Diagnostic Library",
            "description": description,
            "url": canonical,
            "mainEntity": {"@type": "ItemList", "numberOfItems": len(entries)},
        }],
    }
    body = f'''
  <section class="library-page">
    {crumb_html}
    <header class="library-hero library-index-hero">
      <span class="section-kicker">Built into PitMedic</span>
      <h1>PitMedic Diagnostic Library</h1>
      <p>Search known sim-racing errors, launch failures, crashes, configuration problems, and companion-software issues. Each guide explains diagnostic evidence, available repairs, or version-specific manual checks.</p>
      <div class="library-count"><strong>{len(entries)}</strong><span>diagnostic, repair and guidance records</span></div>
    </header>
    <nav class="library-topics" aria-label="Browse troubleshooting guides by simulator">
      <a href="?software=iracing#library-results" data-product-filter="iracing"><strong>iRacing troubleshooting</strong><span>Loading errors, updates, UI, services, and anti-cheat</span></a>
      <a href="?software=le%20mans%20ultimate#library-results" data-product-filter="le mans ultimate"><strong>Le Mans Ultimate troubleshooting</strong><span>Startup, content, memory, plugins, and overlays</span></a>
      <a href="?software=assetto%20corsa%20competizione#library-results" data-product-filter="assetto corsa competizione"><strong>ACC troubleshooting</strong><span>Controls, force feedback, profiles, and Steam files</span></a>
      <a href="?software=automobilista%202#library-results" data-product-filter="automobilista 2"><strong>Automobilista 2 troubleshooting</strong><span>Graphics, VR, controllers, force feedback, and profiles</span></a>
      <a href="?software=raceroom%20racing%20experience#library-results" data-product-filter="raceroom racing experience"><strong>RaceRoom troubleshooting</strong><span>Error 503, startup, graphics, cache, and configuration</span></a>
      <a href="?software=assetto%20corsa%20evo#library-results" data-product-filter="assetto corsa evo"><strong>Assetto Corsa EVO troubleshooting</strong><span>Startup, video settings, profiles, and Steam content</span></a>
    </nav>
    <section class="library-controls" aria-label="Filter diagnostics">
      <label><span>Search</span><input id="library-search" type="search" placeholder="Error message, symptom, or software" autocomplete="off" /></label>
      <label><span>Software</span><select id="library-product"><option value="">All software</option>{product_options}</select></label>
      <label><span>Coverage</span><select id="library-repair"><option value="">All coverage</option><option value="automatic repair available">Automatic repair available</option><option value="approval required">Approval required</option><option value="guided diagnosis">Guided diagnosis</option></select></label>
    </section>
    <p class="library-results" id="library-results" role="status" aria-live="polite" tabindex="-1"><strong id="library-visible-count">{len(entries)}</strong> records shown</p>
    <div class="library-grid" id="library-grid">{''.join(cards)}</div>
    <p class="library-empty" id="library-empty" hidden>No matching diagnostic records. Try a broader search or clear a filter.</p>
  </section>
  <script>
    (() => {{
      const search = document.querySelector('#library-search');
      const product = document.querySelector('#library-product');
      const repair = document.querySelector('#library-repair');
      const cards = [...document.querySelectorAll('.library-card')];
      const count = document.querySelector('#library-visible-count');
      const empty = document.querySelector('#library-empty');
      const topics = [...document.querySelectorAll('[data-product-filter]')];
      const results = document.querySelector('#library-results');
      const readFilters = () => {{
        const params = new URLSearchParams(window.location.search);
        const requestedProduct = (params.get('software') || '').replaceAll('-', ' ').toLowerCase();
        product.value = [...product.options].some(option => option.value === requestedProduct) ? requestedProduct : '';
        const requestedRepair = params.get('coverage') || '';
        repair.value = [...repair.options].some(option => option.value === requestedRepair) ? requestedRepair : '';
        search.value = params.get('q') || '';
      }};
      const writeFilters = (push = false) => {{
        const url = new URL(window.location.href);
        for (const [key, value] of [['software', product.value], ['coverage', repair.value], ['q', search.value.trim()]]) {{
          if (value) url.searchParams.set(key, value);
          else url.searchParams.delete(key);
        }}
        if (push) url.hash = 'library-results';
        window.history[push ? 'pushState' : 'replaceState'](null, '', url);
      }};
      const filter = () => {{
        const query = search.value.trim().toLowerCase();
        let visible = 0;
        cards.forEach(card => {{
          const show = (!query || card.dataset.search.includes(query)) && (!product.value || card.dataset.product === product.value) && (!repair.value || card.dataset.repair === repair.value);
          card.hidden = !show;
          if (show) visible += 1;
        }});
        count.textContent = visible;
        empty.hidden = visible !== 0;
        topics.forEach(topic => {{
          if (topic.dataset.productFilter === product.value) topic.setAttribute('aria-current', 'true');
          else topic.removeAttribute('aria-current');
        }});
      }};
      const update = () => {{ filter(); writeFilters(); }};
      search.addEventListener('input', update);
      product.addEventListener('change', update);
      repair.addEventListener('change', update);
      topics.forEach(topic => topic.addEventListener('click', event => {{
        if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        product.value = topic.dataset.productFilter;
        search.value = '';
        repair.value = '';
        filter();
        writeFilters(true);
        results.focus({{preventScroll: true}});
        results.scrollIntoView({{block: 'start'}});
      }}));
      window.addEventListener('popstate', () => {{ readFilters(); filter(); }});
      readFilters();
      filter();
    }})();
  </script>
'''
    destination.mkdir(parents=True, exist_ok=True)
    (destination / "index.html").write_text(page_header("PitMedic Diagnostic Library — Known Sim Racing Issues & Fixes", description, canonical, schema) + body + page_footer(), encoding="utf-8")


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
    existing = [
        ("/", TODAY, "weekly", "1.0"),
        ("/simulators/iracing/", "2026-09-07", "monthly", "0.8"),
        ("/simulators/le-mans-ultimate/", "2026-09-07", "monthly", "0.8"),
        ("/simulators/assetto-corsa-competizione/", TODAY, "monthly", "0.8"),
        ("/simulators/automobilista-2/", TODAY, "monthly", "0.8"),
        ("/simulators/raceroom/", TODAY, "monthly", "0.8"),
        ("/simulators/assetto-corsa-evo/", TODAY, "monthly", "0.8"),
        ("/diagnostic-library/", "2026-09-07", "weekly", "0.9"),
    ]
    urls = existing + [(
        f"/diagnostic-library/{entry['id']}/",
        max(str(PUBLIC_CONTENT.get(str(entry["id"]), {}).get("modified", entry["lastVerified"])), str(entry["lastVerified"])),
        "monthly",
        "0.7",
    ) for entry in entries]
    rows = "\n".join(f"  <url><loc>{BASE_URL}{url}</loc><lastmod>{date}</lastmod><changefreq>{frequency}</changefreq><priority>{priority}</priority></url>" for url, date, frequency, priority in urls)
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
