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

export function normalizeSearchText(value: string): string {
  return value
    .normalize("NFKD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, " ")
    .trim()
    .replace(/\s+/g, " ");
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
      !phraseMatches(searchable, query.track) ||
      !phraseMatches(searchable, query.car) ||
      (query.layout.length > 0 && !phraseMatches(searchable, query.layout))
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

function phraseMatches(searchable: string, phrase: string): boolean {
  const normalized = normalizeSearchText(phrase);
  if (normalized.length === 0) return true;
  if (searchable.includes(normalized)) return true;
  const tokens = normalized
    .split(" ")
    .filter((token) => token.length > 1 && !IGNORED_TOKENS.has(token));
  return tokens.length > 0 && tokens.every((token) => searchable.includes(token));
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
