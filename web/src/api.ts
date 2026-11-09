// Thin fetch wrapper. Auth is a same-origin HttpOnly cookie, so nothing to attach here.
export class ApiError extends Error {
  // detail: the server's explanation (ProblemDetails "detail", or a plain-text body), if any.
  constructor(public status: number, message: string, public detail?: string) {
    super(message);
  }
}

export async function api<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T> {
  const { json, ...rest } = init ?? {};
  const res = await fetch(path, {
    ...rest,
    headers: json !== undefined ? { 'Content-Type': 'application/json', ...rest.headers } : rest.headers,
    body: json !== undefined ? JSON.stringify(json) : rest.body,
  });
  // Read as text first: some endpoints answer with an empty body.
  const text = await res.text();
  if (!res.ok) throw new ApiError(res.status, `${res.status} ${res.statusText}`, problemDetail(text));
  return (text ? JSON.parse(text) : undefined) as T;
}

export function problemDetail(text: string) {
  if (!text) return undefined;
  try {
    const body = JSON.parse(text);
    return typeof body === 'string' ? body : (body.detail ?? body.title);
  } catch {
    return text.length < 300 ? text : undefined;
  }
}

export function errorMessage(err: unknown, fallback = 'Noget gik galt. Prøv igen.') {
  if (err instanceof ApiError) return err.detail ?? fallback;
  return fallback;
}

export type Me = { name: string };

export type Category = 'Music' | 'Theatre' | 'Comedy' | 'Film' | 'Sport' | 'Other';
export type Zone = 'Copenhagen' | 'Denmark' | 'Abroad';
export type Reaction = 'Saved' | 'Maybe' | 'NotInterested';
export type EventView = 'new' | 'forYou' | 'saved';

export type CultureEvent = {
  id: string;
  source: string;
  title: string;
  category: Category;
  genre: string | null;
  /** yyyy-MM-dd, local to the venue. */
  date: string | null;
  start: string | null;
  venue: string | null;
  city: string | null;
  countryCode: string | null;
  priceFrom: number | null;
  currency: string | null;
  url: string;
  saleStart: string | null;
  imageUrl: string | null;
  firstSeenUtc: string;
};

export type EventItem = { event: CultureEvent; zone: Zone; reaction: Reaction | null; isNew: boolean };
export type EventList = { lastVisitUtc: string | null; events: EventItem[] };

export type TasteProfile = { about: string; sport: string };

export type SourceResult = { source: string; fetched: number; error: string | null };
export type ScanResult = { sources: SourceResult[]; added: number; total: number };

export const categoryNames: Record<Category, string> = {
  Music: 'Musik',
  Theatre: 'Teater',
  Comedy: 'Stand-up',
  Film: 'Film',
  Sport: 'Sport',
  Other: 'Andet',
};

export const zoneNames: Record<Zone, string> = {
  Copenhagen: 'København',
  Denmark: 'Resten af Danmark',
  Abroad: 'Udlandet',
};

export const fetchEvents = (view: EventView) => api<EventList>(`/api/events?view=${view}`);

export const setReaction = (id: string, reaction: Reaction | null) =>
  api<void>(`/api/events/${encodeURIComponent(id)}/reaction`, { method: 'PUT', json: { reaction } });
