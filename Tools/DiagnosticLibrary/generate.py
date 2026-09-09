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
RELEASE_URL = "https://github.com/rholmes426/PitMedic/releases/download/v0.6.0.17/PitMedic-Setup-x64.exe"
RELEASE_VERSION_MATCH = re.search(r"/v([^/]+)/PitMedic-Setup-x64\.exe$", RELEASE_URL)
if not RELEASE_VERSION_MATCH:
    raise RuntimeError("Diagnostic Library release URL does not contain a valid PitMedic version")
RELEASE_VERSION = RELEASE_VERSION_MATCH.group(1)
LOGO_URL = "https://pitmedic.com/assets/brand/pitmedic-icon-v2.png"
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
PUBLIC_CONTENT = {
    "iracing-missing-file-privileges": {
        "heading": "iRacing Missing File Privileges during a Steam update",
        "modified": "2026-09-07",
        "title": "iRacing Missing File Privileges: Safe Steam Fix | PitMedic",
        "description": "Steam says Missing File Privileges while updating iRacing? Learn why the Helper Service can lock files, what to check, and how PitMedic safely restarts the update.",
        "intro": "Steam can stop an iRacing update with a Missing File Privileges message when the iRacing Helper Service still has installation files open. PitMedic confirms that specific combination before offering a controlled recovery.",
        "meaning": "This message does not necessarily mean your Windows account has lost access to the whole iRacing installation. During an update, the background Helper Service can keep an iRacing file in use, preventing Steam from replacing it. A Content File Locked message can have the same underlying cause.",
        "checks": [
            "Close the iRacing simulator and finish any active session before changing update files.",
            "Confirm the error appears while Steam is updating iRacing, rather than during an unrelated Steam download.",
            "Check whether iRacing.com Helper Service is still running in Windows Task Manager.",
            "Allow time for Steam validation: on a large installation it can take more than two minutes.",
        ],
        "result": "Steam should be able to validate or update the installation after the Helper Service releases its file handles. PitMedic restarts the service afterward. If Steam still reports the error, stop rather than repeatedly deleting content and continue with iRacing's official support guidance.",
        "related": ["iracing-content-file-locked", "iracing-helper-service", "iracing-update-verification", "iracing-car-loading-errors"],
    },
    "iracing-content-file-locked": {
        "heading": "iRacing Content File Locked during a Steam update",
        "modified": "2026-09-07",
        "title": "iRacing Content File Locked: Fix the Steam Update Error | PitMedic",
        "description": "Fix Steam's Content File Locked error for iRacing by identifying a running Helper Service, releasing the locked files, and safely resuming validation.",
        "intro": "A Content File Locked error means Steam could not replace part of the iRacing installation because another process still had the file open. PitMedic checks for the iRacing Helper Service before recommending a restart of the update.",
        "meaning": "iRacing's Helper Service normally runs in the background. If it remains active during a Steam update, Windows can prevent Steam from changing a file that the service is using. The repair should therefore target the process holding the file, not erase unrelated settings or vehicle data.",
        "checks": [
            "Close the simulator, iRacing UI, and any active Steam update dialog.",
            "Confirm Content File Locked names the iRacing installation.",
            "Check Task Manager for iRacing.com Helper Service before using broader repair steps.",
            "Preserve user setups, paints, and replays; they are not part of this targeted recovery.",
        ],
        "result": "Once the Helper Service is no longer holding the installation open, Steam should be able to complete validation or download the affected file. PitMedic verifies that the update was started and restores the service afterward.",
        "related": ["iracing-missing-file-privileges", "iracing-helper-service", "iracing-update-verification", "iracing-content-corruption"],
    },
    "iracing-loading-error-3": {
        "heading": "iRacing Loading Error 3",
        "modified": "2026-09-07",
        "title": "iRacing Loading Error 3: Causes and Recovery Steps | PitMedic",
        "description": "Troubleshoot iRacing Loading Error 3, rule out car and track problems, preserve Documents\\iRacing, and safely generate a clean configuration.",
        "intro": "iRacing Loading Error 3 has several possible causes. PitMedic treats a full configuration reset as a last step, after checking for a specific car or track problem that can be repaired more narrowly.",
        "meaning": "The error can follow damaged car or track content, an unsupported car-and-track combination, or a damaged Documents\\iRacing profile. Because those causes require different fixes, seeing Error 3 alone is not enough reason to remove the entire profile.",
        "checks": [
            "Try another known-good car and track combination to see whether the failure is content-specific.",
            "Resolve a numbered car or track loading error first if iRacing reports one alongside Error 3.",
            "Close iRacing before moving its Documents folder.",
            "Keep setups, replays, paints, and controller information in the preserved backup.",
        ],
        "result": "A clean profile will make iRacing request graphics configuration and controller calibration again. If the simulator then loads, restore only the personal setups, replays, and paints you need from the backup instead of copying the damaged configuration back wholesale.",
        "related": ["iracing-car-loading-errors", "iracing-track-loading-errors", "iracing-renderer-config", "iracing-content-corruption"],
    },
    "iracing-car-loading-errors": {
        "heading": "iRacing Loading Error 22 or 71",
        "modified": "2026-09-07",
        "title": "iRacing Loading Error 22 or 71: Repair Car Files | PitMedic",
        "description": "Loading Error 22 or 71 usually points to damaged or outdated iRacing car content. Identify the affected car, preserve unrelated files, and redownload only what is needed.",
        "intro": "iRacing Loading Errors 22 and 71 usually point to car content that is missing, outdated, or corrupt. PitMedic favors the smallest repair: replace the affected car before considering a reset of shared car metadata.",
        "meaning": "If one car fails while other combinations load, the problem is likely limited to that vehicle's installed files. Error 71 can also involve shared car metadata or the pace-car safety directory. A failure across every car requires a broader content check than a single-car error.",
        "checks": [
            "Record which car or multiclass session triggers the loading error.",
            "Test a different car at the same track to separate car content from track content.",
            "Close iRacing before changing files in its installation directory.",
            "Start with the named car; do not remove the full cars directory unless the problem affects all cars.",
        ],
        "result": "iRacing should offer the removed car or shared metadata as an update. After it is downloaded again, retry the same car-and-track combination and confirm that other installed content remains available.",
        "related": ["iracing-loading-error-3", "iracing-track-loading-errors", "iracing-content-corruption", "iracing-update-verification"],
    },
    "iracing-eac-error73": {
        "heading": "iRacing Error 73 / Easy Anti-Cheat failure",
        "modified": "2026-09-07",
        "title": "iRacing Error 73: Repair Easy Anti-Cheat | PitMedic",
        "description": "Troubleshoot iRacing Error 73 and repair the EOS Easy Anti-Cheat installation after confirming the startup error and checking for duplicate iRacing installs.",
        "intro": "iRacing Error 73 is a startup failure associated with the EOS version of Easy Anti-Cheat. PitMedic confirms the error signature before offering iRacing's supported uninstall, reinstall, and repair workflow.",
        "meaning": "The failure occurs before a normal simulator session begins, so graphics, car, and track resets are unlikely to address it. iRacing also warns that duplicate installations can leave the anti-cheat repair pointed at the wrong simulator path.",
        "checks": [
            "Close iRacing before running the anti-cheat repair.",
            "Confirm the message explicitly names Error 73, Easy Anti-Cheat, or EOS.",
            "Check for more than one iRacing installation and identify the copy you actually launch.",
            "Use the Easy Anti-Cheat tools included with that installation rather than an unrelated download.",
        ],
        "result": "After the EOS Easy Anti-Cheat service is repaired, launch the same iRacing installation again. If Error 73 remains, verify that shortcuts and Steam point to the same installation before repeating the repair.",
        "related": ["iracing-windows-integrity", "iracing-compatibility-flags", "iracing-helper-service", "iracing-loading-error-3"],
    },
    "companion-moza-clean-recovery": {
        "heading": "MOZA Pit House not opening or crashing",
        "modified": "2026-09-07",
        "title": "MOZA Pit House Not Opening or Crashing: Recovery Guide | PitMedic",
        "description": "MOZA Pit House won't open, crashed, or left a stale process? See the safe restart sequence PitMedic uses without resetting profiles or changing device firmware.",
        "intro": "When MOZA Pit House crashes or will not reopen, an old Pit House process may still be running in the background. PitMedic can close only those remaining processes, relaunch the installed application, and verify that it stays open.",
        "meaning": "This recovery is intentionally limited to a confirmed application crash or stale process. It does not flash firmware, reset wheel profiles, remove drivers, or alter simulator settings. Firmware, device-detection, and network errors need their own diagnosis.",
        "checks": [
            "Close every supported racing simulator before restarting wheelbase software.",
            "Confirm Pit House has crashed, stopped responding, or will not reopen because a process remains active.",
            "Do not interrupt a firmware update or use this recovery while MOZA's offline firmware tool is running.",
            "If Pit House opens, use its Report Error feature for a repeatable crash and retain the report number.",
        ],
        "result": "Pit House should relaunch from its validated installed location and remain running. If it closes again, PitMedic records the failed recovery without changing profiles or firmware; the next step is MOZA's built-in error report and official support guidance.",
        "related": ["companion-logitech-ghub-service-recovery", "companion-simucube-clean-recovery", "companion-fanatec-process-recovery", "companion-simagic-clean-recovery"],
    },
}


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
            "displayKind": "Simulator repair",
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
    for entry_id, content in PUBLIC_CONTENT.items():
        missing_fields = sorted({"heading", "modified", "title", "description", "intro", "meaning", "checks", "result", "related"} - set(content))
        if missing_fields:
            raise ValueError(f"{entry_id} public content is missing fields: {missing_fields}")
        unknown_related = sorted(set(content["related"]) - known_ids)
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
  <link rel="icon" href="/assets/brand/pitmedic-v2.ico" sizes="any" />
  <link rel="apple-touch-icon" href="/assets/brand/apple-touch-icon-v2.png" />
  <link rel="stylesheet" href="/styles.css" />
  <script type="application/ld+json">{json.dumps(structured_data, separators=(",", ":"), ensure_ascii=False).replace("</", "<\\/")}</script>
</head>
<body>
<main>
  <header class="site-header">
    <a class="brand" href="/" aria-label="PitMedic home"><img src="{LOGO_URL}" alt="" /><span>PitMedic</span></a>
    <nav aria-label="Primary navigation"><a href="/#how-it-works">How it works</a><a href="/#simulators">Simulators</a><a class="active" href="/diagnostic-library/">Diagnostic Library</a><a href="/#about">About</a></nav>
    <div class="header-actions"><a class="header-support" href="https://paypal.me/PitMedicApp" target="_blank" rel="noreferrer">Support PitMedic</a><a class="header-cta" href="/">Home</a></div>
  </header>'''


def page_footer() -> str:
    return f'''  <footer>
    <a class="brand" href="/"><img src="{LOGO_URL}" alt="" /><span>PitMedic</span></a>
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
    if "diagnostic" in lower or "community-derived" in lower:
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
                "dateModified": editorial.get("modified", entry["lastVerified"]),
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
        explanation_html = f'''        <section class="diagnostic-card diagnostic-guide"><h2>What this problem means</h2><p>{esc(meaning)}</p>
          <h3>Before you repair it</h3><ul class="guide-checks">{checks_html}</ul>
          <h3>How to confirm the result</h3><p>{esc(result)}</p>
        </section>
'''
    body = f'''
  <article class="library-page issue-page">
    {crumb_html}
    <header class="library-hero">
      <span class="section-kicker">{esc(entry["displayKind"])}</span>
      <p class="library-product">{esc(product)}</p>
      <h1>{esc(display_issue)}</h1>
      <p>{esc(intro)}</p>
      <div class="issue-status"><span>{esc(repair_label(str(entry["safety"])))}</span><span>Active</span><span>Reviewed {esc(entry["lastVerified"])}</span></div>
    </header>
    <div class="diagnostic-layout">
      <div class="diagnostic-main">
{explanation_html}        <section class="diagnostic-card"><h2>How PitMedic recognizes it</h2><p>{esc(entry["detection"])}</p>{details}</section>
        <section class="diagnostic-card"><h2>Built-in response</h2><p>{esc(entry["repair"])}</p><div class="safety-note"><strong>Repair safety</strong><span>{esc(entry["safety"])}</span></div></section>
        <section class="diagnostic-card"><h2>Verification sources</h2><p>PitMedic prioritizes vendor documentation and labels community findings separately.</p><ul class="source-list">{source_items}</ul></section>
      </div>
      <aside class="library-aside"><h2>Let PitMedic check it</h2><p>PitMedic compares the evidence on your PC with this record. It only offers a repair when the relevant conditions are present.</p><a class="button button-primary" href="{RELEASE_URL}">Download v{RELEASE_VERSION}</a>{product_link}<small>Free and open source · Windows 10/11</small></aside>
    </div>
    <nav class="related-diagnostics" aria-label="Related diagnostics"><h2>{"Other companion software recoveries" if is_companion else "More for " + esc(product)}</h2><div>{related_html}</div></nav>
  </article>
'''
    destination.mkdir(parents=True, exist_ok=True)
    page_title = str(editorial.get("title") or (f"{product} Crash & Stale Process Recovery | PitMedic" if is_companion else f"{issue} — {product} | PitMedic"))
    (destination / "index.html").write_text(page_header(page_title, description, canonical, schema) + body + page_footer(), encoding="utf-8")


def write_index(entries: list[dict[str, object]], destination: Path) -> None:
    canonical = f"{BASE_URL}/diagnostic-library/"
    description = "Browse 60 sim-racing troubleshooting guides for iRacing, Le Mans Ultimate, ACC, AMS2, RaceRoom, Assetto Corsa EVO, and companion software."
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
      <p>Search known sim-racing errors, launch failures, crashes, configuration problems, and companion-software issues. Each guide explains the evidence PitMedic checks and the safe response available in the app.</p>
      <div class="library-count"><strong>{len(entries)}</strong><span>active diagnostic and repair records</span></div>
    </header>
    <nav class="library-topics" aria-label="Browse troubleshooting guides by simulator">
      <a href="/simulators/iracing/"><strong>iRacing troubleshooting</strong><span>Loading errors, updates, UI, services, and anti-cheat</span></a>
      <a href="/simulators/le-mans-ultimate/"><strong>Le Mans Ultimate troubleshooting</strong><span>Startup, content, memory, plugins, and overlays</span></a>
      <a href="/simulators/assetto-corsa-competizione/"><strong>ACC troubleshooting</strong><span>Controls, force feedback, profiles, and Steam files</span></a>
      <a href="/simulators/automobilista-2/"><strong>Automobilista 2 troubleshooting</strong><span>Graphics, VR, controllers, force feedback, and profiles</span></a>
      <a href="/simulators/raceroom/"><strong>RaceRoom troubleshooting</strong><span>Error 503, startup, graphics, cache, and configuration</span></a>
      <a href="/simulators/assetto-corsa-evo/"><strong>Assetto Corsa EVO troubleshooting</strong><span>Startup, video settings, profiles, and Steam content</span></a>
    </nav>
    <section class="library-controls" aria-label="Filter diagnostics">
      <label><span>Search</span><input id="library-search" type="search" placeholder="Error message, symptom, or software" autocomplete="off" /></label>
      <label><span>Software</span><select id="library-product"><option value="">All software</option>{product_options}</select></label>
      <label><span>Coverage</span><select id="library-repair"><option value="">All coverage</option><option value="automatic repair available">Automatic repair available</option><option value="approval required">Approval required</option><option value="guided diagnosis">Guided diagnosis</option></select></label>
    </section>
    <p class="library-results"><strong id="library-visible-count">{len(entries)}</strong> records shown</p>
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
      const requestedProduct = new URLSearchParams(window.location.search).get('software');
      if (requestedProduct) {{
        const normalized = requestedProduct.replaceAll('-', ' ').toLowerCase();
        if ([...product.options].some(option => option.value === normalized)) product.value = normalized;
      }}
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
      }};
      search.addEventListener('input', filter);
      product.addEventListener('change', filter);
      repair.addEventListener('change', filter);
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
        str(PUBLIC_CONTENT.get(str(entry["id"]), {}).get("modified", entry["lastVerified"])),
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
    if len(entries) != 60:
        raise ValueError(f"Expected 60 public records, found {len(entries)}")
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
        print("Generated 60 Diagnostic Library records.")
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
