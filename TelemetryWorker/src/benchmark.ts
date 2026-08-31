export interface BenchmarkQuery {
  sim: string;
  track: string;
  layout: string;
  car: string;
}

export interface WebLapCandidate {
  title: string;
  description: string;
  channelTitle: string;
  videoId: string;
}

export interface SelectedBenchmark {
  lapSeconds: number;
  sourceKind: "web";
  sourceName: string;
  sourceUrl: string;
  confidence: "exact_match";
}

const IGNORED_TOKENS = new Set([
  "a",
  "an",
  "and",
  "at",
  "edition",
  "experience",
  "for",
  "of",
  "racing",
  "simulator",
  "the",
]);

const TRACK_IGNORED_TOKENS = new Set([
  ...IGNORED_TOKENS,
  "circuit",
  "course",
  "de",
  "international",
  "motor",
  "raceway",
  "speedway",
]);

const CAR_IGNORED_TOKENS = new Set([
  ...IGNORED_TOKENS,
  "car",
  "cup",
  "global",
  "series",
]);

export function normalizeSearchText(value: string): string {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/['’]s\b/gi, "s")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim()
    .replace(/\s+/g, " ");
}

export function buildYouTubeSearchQuery(query: BenchmarkQuery): string {
  return [
    preferredSimSearchTerm(query.sim),
    significantPhrase(query.track, TRACK_IGNORED_TOKENS),
    significantPhrase(query.layout, IGNORED_TOKENS),
    significantPhrase(query.car, CAR_IGNORED_TOKENS),
    "hotlap lap guide",
  ]
    .filter(Boolean)
    .join(" ");
}

export function combinationKey(query: BenchmarkQuery): string {
  return [query.sim, query.track, query.layout, query.car]
    .map(normalizeSearchText)
    .join("|");
}

export function selectFastestWebLap(
  query: BenchmarkQuery,
  candidates: WebLapCandidate[],
): SelectedBenchmark | null {
  let selected: SelectedBenchmark | null = null;

  for (const candidate of candidates) {
    const searchable = normalizeSearchText(
      `${candidate.title} ${candidate.description}`,
    );
    if (
      !simMatches(searchable, query.sim) ||
      !trackMatches(searchable, query.track) ||
      !phraseMatches(searchable, query.car, CAR_IGNORED_TOKENS) ||
      (query.layout.length > 0 && !layoutMatches(searchable, query.layout))
    ) {
      continue;
    }

    // Titles are the only dependable structured surface in a general video
    // search result. Description timestamps often describe chapters, not laps.
    const times = extractLapTimes(candidate.title);
    for (const lapSeconds of times) {
      if (lapSeconds < 30 || lapSeconds > 1_800) continue;
      if (selected && selected.lapSeconds <= lapSeconds) continue;
      selected = {
        lapSeconds,
        sourceKind: "web",
        sourceName: `YouTube · ${candidate.channelTitle}`,
        sourceUrl: `https://www.youtube.com/watch?v=${encodeURIComponent(candidate.videoId)}`,
        confidence: "exact_match",
      };
    }
  }

  return selected;
}

export function extractLapTimes(value: string): number[] {
  const times: number[] = [];
  const pattern = /\b(\d{1,2}):([0-5]\d)[.,](\d{1,3})\b/g;
  for (const match of value.matchAll(pattern)) {
    const [, minutesText, secondsText, fractionText] = match;
    if (!minutesText || !secondsText || !fractionText) continue;
    const minutes = Number(minutesText);
    const seconds = Number(secondsText);
    const fraction = Number(fractionText.padEnd(3, "0"));
    times.push(minutes * 60 + seconds + fraction / 1_000);
  }
  return times;
}

function phraseMatches(
  searchable: string,
  phrase: string,
  ignoredTokens: Set<string> = IGNORED_TOKENS,
): boolean {
  const normalized = normalizeSearchText(phrase);
  if (normalized.length === 0) return true;
  if (searchable.includes(normalized)) return true;
  const tokens = significantTokens(normalized, ignoredTokens);
  if (tokens.length === 0) return false;
  if (tokens.every((token) => containsToken(searchable, token))) return true;
  return compact(searchable).includes(compact(tokens.join(" ")));
}

function trackMatches(searchable: string, track: string): boolean {
  if (phraseMatches(searchable, track, TRACK_IGNORED_TOKENS)) return true;
  const tokens = significantTokens(track, TRACK_IGNORED_TOKENS);
  if (tokens.length === 0) return false;
  const matched = tokens.filter((token) => containsToken(searchable, token)).length;
  return matched >= 1 && matched / tokens.length >= 0.5;
}

function layoutMatches(searchable: string, layout: string): boolean {
  if (phraseMatches(searchable, layout)) return true;
  const normalized = normalizeSearchText(layout);
  const aliases = new Set([
    normalized.replace(/\bgrand prix\b/g, "gp"),
    normalized.replace(/\binternational\b/g, "intl"),
    normalized.replace(/\bfull course\b/g, "full"),
  ]);
  return [...aliases].some(
    (alias) => alias !== normalized && phraseMatches(searchable, alias),
  );
}

function significantPhrase(value: string, ignoredTokens: Set<string>): string {
  return significantTokens(value, ignoredTokens).join(" ");
}

function significantTokens(value: string, ignoredTokens: Set<string>): string[] {
  return normalizeSearchText(value)
    .split(" ")
    .filter(
      (token) =>
        (token.length > 1 || /^\d+$/.test(token)) && !ignoredTokens.has(token),
    );
}

function containsToken(searchable: string, token: string): boolean {
  return ` ${searchable} `.includes(` ${token} `);
}

function compact(value: string): string {
  return value.replace(/\s+/g, "");
}

function preferredSimSearchTerm(sim: string): string {
  const normalized = normalizeSearchText(sim);
  const terms: Record<string, string> = {
    "le mans ultimate": "LMU",
    iracing: "iRacing",
    "assetto corsa evo": "Assetto Corsa EVO",
    "raceroom racing experience": "RaceRoom",
    "assetto corsa competizione": "ACC",
    "automobilista 2": "AMS2",
  };
  return terms[normalized] ?? sim;
}

function simMatches(searchable: string, sim: string): boolean {
  const normalized = normalizeSearchText(sim);
  const aliases: Record<string, string[]> = {
    "le mans ultimate": ["le mans ultimate", "lmu"],
    iracing: ["iracing"],
    "assetto corsa evo": ["assetto corsa evo", "ac evo", "acevo"],
    "raceroom racing experience": ["raceroom", "r3e"],
    "assetto corsa competizione": ["assetto corsa competizione", "acc"],
    "automobilista 2": ["automobilista 2", "ams2"],
  };
  return (aliases[normalized] ?? [normalized]).some((alias) =>
    searchable.includes(alias),
  );
}
