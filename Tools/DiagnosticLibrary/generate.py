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
RELEASE_URL = "https://github.com/rholmes426/PitMedic/releases/download/v0.6.0.11/PitMedic-Setup-x64.exe"
LOGO_URL = "https://raw.githubusercontent.com/rholmes426/PitMedic/main/Source/PitMedic/Assets/PitMedic_256.png"
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
  <link rel="icon" href="{LOGO_URL}" />
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
    description = (f"How PitMedic detects and performs a controlled recovery after a {product} crash or stale process."
                   if is_companion else f"How PitMedic detects and safely responds to {issue.lower()} in {product}.")
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
                "dateModified": entry["lastVerified"],
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
    related = [item for item in all_entries if item["id"] != entry["id"] and (
        item["displayKind"] == entry["displayKind"] if is_companion else item["product"] == product
    )][:4]
    related_html = "".join(
        f'<a href="/diagnostic-library/{esc(item["id"])}/"><strong>{esc(item["issue"])}</strong><span>{esc(repair_label(str(item["safety"])))}</span></a>'
        for item in related
    )
    sim_slug = GAME_SLUGS.get(product)
    product_link = f'<a class="button button-secondary" href="/simulators/{sim_slug}/">View {esc(product)} coverage</a>' if sim_slug else '<a class="button button-secondary" href="/diagnostic-library/">Browse all diagnostics</a>'
    body = f'''
  <article class="library-page issue-page">
    {crumb_html}
    <header class="library-hero">
      <span class="section-kicker">{esc(entry["displayKind"])}</span>
      <p class="library-product">{esc(product)}</p>
      <h1>{esc(display_issue)}</h1>
      <p>PitMedic contains a dedicated diagnostic record for this problem, including the evidence required before it recommends a repair.</p>
      <div class="issue-status"><span>{esc(repair_label(str(entry["safety"])))}</span><span>Active</span><span>Reviewed {esc(entry["lastVerified"])}</span></div>
    </header>
    <div class="diagnostic-layout">
      <div class="diagnostic-main">
        <section class="diagnostic-card"><span class="card-number">01</span><h2>How PitMedic recognizes it</h2><p>{esc(entry["detection"])}</p>{details}</section>
        <section class="diagnostic-card"><span class="card-number">02</span><h2>Built-in response</h2><p>{esc(entry["repair"])}</p><div class="safety-note"><strong>Repair safety</strong><span>{esc(entry["safety"])}</span></div></section>
        <section class="diagnostic-card"><span class="card-number">03</span><h2>Verification sources</h2><p>PitMedic prioritizes vendor documentation and labels community findings separately.</p><ul class="source-list">{source_items}</ul></section>
      </div>
      <aside class="library-aside"><h2>Let PitMedic check it</h2><p>PitMedic compares the evidence on your PC with this record. It only offers a repair when the relevant conditions are present.</p><a class="button button-primary" href="{RELEASE_URL}">Download v0.6.0.11</a>{product_link}<small>Free and open source · Windows 10/11</small></aside>
    </div>
    <nav class="related-diagnostics" aria-label="Related diagnostics"><h2>{"Other companion software recoveries" if is_companion else "More for " + esc(product)}</h2><div>{related_html}</div></nav>
  </article>
'''
    destination.mkdir(parents=True, exist_ok=True)
    page_title = f"{product} Crash & Stale Process Recovery | PitMedic" if is_companion else f"{issue} — {product} | PitMedic"
    (destination / "index.html").write_text(page_header(page_title, description, canonical, schema) + body + page_footer(), encoding="utf-8")


def write_index(entries: list[dict[str, object]], destination: Path) -> None:
    canonical = f"{BASE_URL}/diagnostic-library/"
    description = "Browse the simulator and companion-software problems PitMedic can recognize, explain, and safely repair."
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
      <p>Known simulator and companion-software problems, the evidence PitMedic looks for, and the safe responses available inside the app.</p>
      <div class="library-count"><strong>{len(entries)}</strong><span>active diagnostic and repair records</span></div>
    </header>
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
        ("/simulators/iracing/", TODAY, "monthly", "0.8"),
        ("/simulators/le-mans-ultimate/", TODAY, "monthly", "0.8"),
        ("/simulators/assetto-corsa-competizione/", TODAY, "monthly", "0.8"),
        ("/simulators/automobilista-2/", TODAY, "monthly", "0.8"),
        ("/simulators/raceroom/", TODAY, "monthly", "0.8"),
        ("/simulators/assetto-corsa-evo/", TODAY, "monthly", "0.8"),
        ("/diagnostic-library/", TODAY, "weekly", "0.9"),
    ]
    urls = existing + [(f"/diagnostic-library/{entry['id']}/", str(entry["lastVerified"]), "monthly", "0.7") for entry in entries]
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
