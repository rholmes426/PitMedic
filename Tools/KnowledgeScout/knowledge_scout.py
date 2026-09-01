#!/usr/bin/env python3
"""Read-only source monitor for PitMedic repair knowledge."""

from __future__ import annotations

import argparse
import base64
import hashlib
import html
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
from datetime import date, datetime, timedelta, timezone
from typing import Any
from urllib.parse import urljoin, urlsplit
from urllib.request import Request, build_opener, HTTPRedirectHandler


MAX_BYTES = 2 * 1024 * 1024
MAX_LINKS_PER_SOURCE = 12
STATE_PATTERN = re.compile(r"<!-- pitmedic-knowledge-state:([A-Za-z0-9_=-]+) -->")
IMPLEMENTED_ID_PATTERN = re.compile(r'\bId\s*=\s*"([a-z0-9-]+)"')
COMPANION_ID_PATTERN = re.compile(r'"(companion-[a-z0-9-]+)"')
URL_PATTERN = re.compile(r'https://[^"\s]+')
HARM_TERMS = (
    "data loss",
    "corrupts",
    "corruption",
    "bricked",
    "brick",
    "unsafe",
    "withdrawn",
    "do not use",
    "no longer supported",
    "security vulnerability",
    "causes a crash",
    "causes crashes",
    "rollback",
)
VALID_STATES = {"active", "guidance", "disabled-for-safety", "version-gated", "superseded"}


class PageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self._ignored_depth = 0
        self._href: str | None = None
        self._anchor: list[str] = []
        self.text_parts: list[str] = []
        self.links: list[tuple[str, str]] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag in {"script", "style", "noscript", "svg"}:
            self._ignored_depth += 1
            return
        if self._ignored_depth == 0 and tag == "a":
            self._href = dict(attrs).get("href")
            self._anchor = []

    def handle_endtag(self, tag: str) -> None:
        if tag in {"script", "style", "noscript", "svg"} and self._ignored_depth:
            self._ignored_depth -= 1
            return
        if self._ignored_depth == 0 and tag == "a" and self._href:
            label = normalize_text(" ".join(self._anchor))
            self.links.append((self._href, label))
            self._href = None
            self._anchor = []

    def handle_data(self, data: str) -> None:
        if self._ignored_depth:
            return
        self.text_parts.append(data)
        if self._href:
            self._anchor.append(data)


class SafeRedirectHandler(HTTPRedirectHandler):
    def __init__(self, allowed_hosts: set[str]) -> None:
        self.allowed_hosts = allowed_hosts

    def redirect_request(self, req: Any, fp: Any, code: int, msg: str, headers: Any, newurl: str) -> Any:
        require_allowed_url(newurl, self.allowed_hosts)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", html.unescape(value)).strip()


def safe_report_text(value: str) -> str:
    """Neutralize issue mentions and raw HTML while retaining readable evidence."""
    return normalize_text(value).replace("@", "＠").replace("<", "‹").replace(">", "›")


def require_allowed_url(url: str, allowed_hosts: set[str]) -> None:
    parsed = urlsplit(url)
    if parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError("only credential-free HTTPS URLs are allowed")
    if parsed.hostname.lower() not in allowed_hosts:
        raise ValueError(f"host is not allowlisted: {parsed.hostname}")


def fetch_text(url: str, allowed_hosts: set[str], timeout: int = 20) -> tuple[str, str]:
    require_allowed_url(url, allowed_hosts)
    opener = build_opener(SafeRedirectHandler(allowed_hosts))
    request = Request(
        url,
        headers={
            "User-Agent": "PitMedic-KnowledgeScout/1.0 (+https://github.com/rholmes426/PitMedic)",
            "Accept": "text/html, application/xhtml+xml, application/xml, text/xml, text/plain, application/json",
        },
    )
    with opener.open(request, timeout=timeout) as response:
        final_url = response.geturl()
        require_allowed_url(final_url, allowed_hosts)
        content_type = response.headers.get_content_type().lower()
        if content_type not in {
            "text/html",
            "application/xhtml+xml",
            "application/xml",
            "text/xml",
            "text/plain",
            "application/json",
        }:
            raise ValueError(f"unsupported content type: {content_type}")
        declared_length = response.headers.get("Content-Length")
        if declared_length and int(declared_length) > MAX_BYTES:
            raise ValueError("response exceeds the 2 MiB limit")
        raw = response.read(MAX_BYTES + 1)
        if len(raw) > MAX_BYTES:
            raise ValueError("response exceeds the 2 MiB limit")
        charset = response.headers.get_content_charset() or "utf-8"
        return raw.decode(charset, errors="replace"), final_url


def parse_page(raw: str, base_url: str) -> tuple[str, list[tuple[str, str]]]:
    parser = PageParser()
    parser.feed(raw)
    text = normalize_text(" ".join(parser.text_parts))
    links: list[tuple[str, str]] = []
    for href, label in parser.links:
        absolute = urljoin(base_url, href)
        if urlsplit(absolute).scheme == "https":
            links.append((absolute, label))
    return text, links


def content_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def candidate_links(
    links: list[tuple[str, str]], keywords: list[str], allowed_hosts: set[str]
) -> list[dict[str, str]]:
    found: dict[str, dict[str, str]] = {}
    lowered_keywords = [item.casefold() for item in keywords]
    for url, label in links:
        parsed = urlsplit(url)
        if parsed.hostname is None or parsed.hostname.lower() not in allowed_hosts:
            continue
        searchable = f"{label} {parsed.path} {parsed.query}".casefold()
        if not any(term in searchable for term in lowered_keywords):
            continue
        clean_url = parsed._replace(fragment="").geturl()
        found[clean_url] = {"url": clean_url, "title": label[:180] or parsed.path[-180:]}
        if len(found) >= MAX_LINKS_PER_SOURCE:
            break
    return list(found.values())


def term_snippets(text: str, terms: tuple[str, ...] = HARM_TERMS, limit: int = 4) -> list[str]:
    lowered = text.casefold()
    snippets: list[str] = []
    for term in terms:
        start = lowered.find(term)
        if start < 0:
            continue
        left = max(0, start - 100)
        right = min(len(text), start + len(term) + 140)
        snippet = normalize_text(text[left:right]).strip(" -|,.;")
        if snippet and snippet not in snippets:
            snippets.append(snippet[:320])
        if len(snippets) >= limit:
            break
    return snippets


def load_prior_state(path: Path | None) -> dict[str, Any]:
    if path is None or not path.exists():
        return {"version": 1, "sources": {}}
    match = STATE_PATTERN.search(path.read_text(encoding="utf-8"))
    if not match:
        return {"version": 1, "sources": {}}
    try:
        return json.loads(base64.urlsafe_b64decode(match.group(1)).decode("utf-8"))
    except (ValueError, json.JSONDecodeError):
        return {"version": 1, "sources": {}}


def encode_state(state: dict[str, Any]) -> str:
    compact = json.dumps(state, separators=(",", ":"), sort_keys=True).encode("utf-8")
    return base64.urlsafe_b64encode(compact).decode("ascii")


def implemented_ids(repo_root: Path) -> set[str]:
    repair_text = (repo_root / "Source/PitMedic/Services/RepairKnowledgeBase.cs").read_text(encoding="utf-8")
    companion_text = (repo_root / "Source/PitMedic/Services/CompanionRecoveryPolicy.cs").read_text(encoding="utf-8")
    return set(IMPLEMENTED_ID_PATTERN.findall(repair_text)) | set(COMPANION_ID_PATTERN.findall(companion_text))


def catalog_validation(repo_root: Path, registry: dict[str, Any], lifecycle: dict[str, Any]) -> list[str]:
    problems: list[str] = []
    entries = lifecycle.get("entries", [])
    lifecycle_ids = [entry.get("id", "") for entry in entries]
    expected = implemented_ids(repo_root)
    missing = sorted(expected - set(lifecycle_ids))
    extra = sorted(set(lifecycle_ids) - expected)
    duplicates = sorted({item for item in lifecycle_ids if lifecycle_ids.count(item) > 1})
    if missing:
        problems.append("Implemented repairs missing lifecycle records: " + ", ".join(missing))
    if extra:
        problems.append("Lifecycle records without implemented repairs: " + ", ".join(extra))
    if duplicates:
        problems.append("Duplicate lifecycle IDs: " + ", ".join(duplicates))
    for entry in entries:
        if entry.get("status") not in VALID_STATES:
            problems.append(f"Invalid lifecycle state for {entry.get('id', '<missing id>')}: {entry.get('status')}")
    policy = lifecycle.get("policy", {})
    required_policy = {
        "ageCanDisable": False,
        "brokenSourceCanDisable": False,
        "automationCanChangeStatus": False,
        "disableRequiresCredibleHarmEvidence": True,
        "retainHistoricalEntries": True,
    }
    for key, expected_value in required_policy.items():
        if policy.get(key) is not expected_value:
            problems.append(f"Lifecycle safety policy {key} must be {str(expected_value).lower()}")
    allowed_hosts = {host.lower() for host in registry.get("allowedHosts", [])}
    source_ids: set[str] = set()
    for source in registry.get("sources", []):
        source_id = source.get("id", "")
        if not source_id or source_id in source_ids:
            problems.append(f"Missing or duplicate source ID: {source_id or '<missing>'}")
        source_ids.add(source_id)
        try:
            require_allowed_url(source.get("url", ""), allowed_hosts)
        except ValueError as error:
            problems.append(f"Invalid source {source_id or '<missing>'}: {error}")
    reference_files = [
        repo_root / "Source/PitMedic/Services/RepairKnowledgeBase.cs",
        repo_root / "Source/PitMedic/Services/CompanionSoftwareKnowledgeBase.cs",
    ]
    for reference_file in reference_files:
        for url in URL_PATTERN.findall(reference_file.read_text(encoding="utf-8")):
            host = urlsplit(url.rstrip(".,")).hostname
            if not host or host.lower() not in allowed_hosts:
                problems.append(f"Knowledge reference host is not allowlisted: {host or url}")
    return problems


def review_reminders(lifecycle: dict[str, Any], today: date) -> list[str]:
    reminders: list[str] = []
    for entry in lifecycle.get("entries", []):
        try:
            verified = date.fromisoformat(entry["lastVerified"])
            due = verified + timedelta(days=int(entry["reviewCadenceDays"]))
        except (KeyError, TypeError, ValueError):
            reminders.append(f"{entry.get('id', '<missing id>')}: invalid review metadata")
            continue
        if due <= today:
            reminders.append(f"{entry['id']}: review due {due.isoformat()} (current state remains `{entry['status']}`)")
    return reminders


def markdown_link(title: str, url: str) -> str:
    safe_title = safe_report_text(title).replace("[", "(").replace("]", ")") or url
    safe_url = url.replace("(", "%28").replace(")", "%29")
    return f"[{safe_title}]({safe_url})"


def build_report(
    repo_root: Path,
    registry: dict[str, Any],
    lifecycle: dict[str, Any],
    prior_state: dict[str, Any],
    offline: bool = False,
    now: datetime | None = None,
    fetcher: Any = fetch_text,
) -> tuple[str, dict[str, Any], bool]:
    now = now or datetime.now(timezone.utc)
    timestamp = now.replace(microsecond=0).isoformat().replace("+00:00", "Z")
    allowed_hosts = {host.lower() for host in registry.get("allowedHosts", [])}
    default_keywords = registry.get("defaultKeywords", [])
    previous_sources = prior_state.get("sources", {})
    next_sources: dict[str, Any] = {}
    changes: list[str] = []
    harms: list[str] = []
    failures: list[str] = []
    baselines = 0

    for source in registry.get("sources", []):
        if not source.get("enabled", True):
            continue
        source_id = source["id"]
        previous = previous_sources.get(source_id, {})
        if offline:
            if previous:
                next_sources[source_id] = previous
            continue
        try:
            raw, final_url = fetcher(source["url"], allowed_hosts)
            text, links = parse_page(raw, final_url)
            digest = content_hash(text)
            discovered = candidate_links(
                links,
                source.get("keywords", default_keywords),
                allowed_hosts,
            ) if source.get("discoverLinks") else []
            known_urls = set(previous.get("links", []))
            current_urls = [item["url"] for item in discovered]
            is_changed = bool(previous.get("hash") and previous.get("hash") != digest)
            if not previous.get("hash"):
                baselines += 1
            if is_changed:
                changes.append(f"**{source['product']}**: source text changed at {markdown_link(source_id, final_url)}")
                for snippet in term_snippets(text):
                    harms.append(f"**{source['product']}**: “{safe_report_text(snippet)}” — {markdown_link(source_id, final_url)}")
            if previous.get("hash"):
                for item in discovered:
                    if item["url"] not in known_urls:
                        changes.append(f"**{source['product']}**: new candidate {markdown_link(item['title'], item['url'])}")
            next_sources[source_id] = {
                "hash": digest,
                "links": current_urls,
                "status": "ok",
                "checked": timestamp,
                "url": final_url,
            }
        except Exception as error:  # Network/parser failure must become review evidence, not a repair-state change.
            failures.append(f"**{source['product']}** `{source_id}`: {type(error).__name__}: {error}. Find a replacement source if needed; existing fixes remain active.")
            retained = dict(previous)
            retained.update({"status": "error", "checked": timestamp, "error": f"{type(error).__name__}: {error}"})
            next_sources[source_id] = retained

    problems = catalog_validation(repo_root, registry, lifecycle)
    reminders = review_reminders(lifecycle, now.date())
    actionable = bool(changes or harms or failures or problems or reminders)
    next_state = {"version": 1, "generated": timestamp, "sources": next_sources}

    lines = [
        "# PitMedic Knowledge Scout — rolling review",
        "",
        f"Last checked: `{timestamp}`",
        "",
        "> This is a read-only discovery report. Age, inactivity, or a broken citation never retires a fix. Only reviewed, credible evidence that using a fix could cause harm can disable or version-gate it.",
        "",
        "## Summary",
        "",
        f"- {len(next_sources)} configured sources checked or retained",
        f"- {len(changes)} new or changed source findings",
        f"- {len(harms)} possible safety/harm signals",
        f"- {len(failures)} source availability issues",
        f"- {len(reminders)} review reminders",
        f"- {len(problems)} catalog validation issues",
    ]
    if baselines:
        lines.append(f"- {baselines} first-run source baselines captured without treating them as new findings")

    def section(title: str, items: list[str], empty: str) -> None:
        lines.extend(["", f"## {title}", ""])
        if items:
            lines.extend(f"- {item}" for item in items[:80])
        else:
            lines.append(empty)

    section("Potential safety/harm signals", harms, "No new safety language detected in changed sources.")
    section("New or changed source material", changes, "No new candidate discussions or changed guidance detected.")
    section("Source availability", failures, "All checked sources were available.")
    section("Review reminders (never automatic retirement)", reminders, "No lifecycle reviews are due.")
    section("Catalog validation", problems, "Implemented repairs, lifecycle records, policy, and source allowlists are consistent.")
    lines.extend([
        "",
        "## Maintainer decision",
        "",
        "For each useful finding, choose: no change, improve detection, add guidance, or add a narrow reversible repair. Version-gate or disable only to prevent evidenced harm. Any repair-state or product change requires a reviewed code change.",
        "",
        f"<!-- pitmedic-knowledge-state:{encode_state(next_state)} -->",
        "",
    ])
    return "\n".join(lines), next_state, actionable


def main() -> int:
    script_path = Path(__file__).resolve()
    default_root = script_path.parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=default_root)
    parser.add_argument("--registry", type=Path)
    parser.add_argument("--lifecycle", type=Path)
    parser.add_argument("--prior-report", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--state-output", type=Path)
    parser.add_argument("--offline", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    root = args.repo_root.resolve()
    registry_path = args.registry or root / "Knowledge/source-registry.json"
    lifecycle_path = args.lifecycle or root / "Knowledge/lifecycle.json"
    registry = json.loads(registry_path.read_text(encoding="utf-8"))
    lifecycle = json.loads(lifecycle_path.read_text(encoding="utf-8"))
    problems = catalog_validation(root, registry, lifecycle)
    if args.validate_only:
        if problems:
            print("\n".join(problems), file=sys.stderr)
            return 1
        print(f"Knowledge catalog valid: {len(lifecycle['entries'])} implemented repairs and {len(registry['sources'])} monitored sources.")
        return 0

    prior_state = load_prior_state(args.prior_report)
    report, state, actionable = build_report(root, registry, lifecycle, prior_state, offline=args.offline)
    if args.report:
        args.report.write_text(report, encoding="utf-8")
    else:
        print(report)
    if args.state_output:
        args.state_output.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")
    print(f"Knowledge Scout complete; actionable={str(actionable).lower()}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
