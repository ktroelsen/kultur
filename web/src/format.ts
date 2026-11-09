import { categoryNames, type Category, type CultureEvent, type Topic } from './api';

const dayFormat = new Intl.DateTimeFormat('da-DK', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
const timeFormat = new Intl.DateTimeFormat('da-DK', { hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Copenhagen' });
const saleFormat = new Intl.DateTimeFormat('da-DK', {
  weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: 'Europe/Copenhagen',
});

/** "lør. 14. mar. 2026 · 19.00" – the time only in Denmark, where it's in our own time zone. */
export function when(e: CultureEvent) {
  if (!e.date) return 'Dato følger';
  const [y, m, d] = e.date.split('-').map(Number);
  const day = dayFormat.format(new Date(y, m - 1, d));
  return e.start && e.countryCode === 'DK' ? `${day} · ${timeFormat.format(new Date(e.start))}` : day;
}

export function where(e: CultureEvent) {
  return [e.venue, e.city].filter(Boolean).join(', ');
}

export function price(e: CultureEvent) {
  if (e.priceFrom == null) return null;
  const amount = Math.round(e.priceFrom).toLocaleString('da-DK');
  return e.currency && e.currency !== 'DKK' ? `fra ${amount} ${e.currency}` : `fra ${amount} kr.`;
}

/** When ticket sales start, if that is still ahead. */
export function upcomingSale(e: CultureEvent, now = new Date()) {
  if (!e.saleStart) return null;
  const start = new Date(e.saleStart);
  return start > now ? saleFormat.format(start) : null;
}

// Ticketmaster's genre names are English; the common ones in Danish.
const genres: Record<string, string> = {
  Football: 'Fodbold', Soccer: 'Fodbold', Handball: 'Håndbold', 'Ice Hockey': 'Ishockey', Hockey: 'Ishockey',
  Motorsports: 'Motorsport', 'Motorsports/Racing': 'Motorsport', Cycling: 'Cykling', Equestrian: 'Ridesport',
  Athletics: 'Atletik', Boxing: 'Boksning', 'Martial Arts': 'Kampsport', Wrestling: 'Wrestling', Golf: 'Golf',
  Theatre: 'Teater', "Children's Theatre": 'Børneteater', Comedy: 'Stand-up', Dance: 'Dans', Classical: 'Klassisk',
  'Fine Art': 'Kunst', 'Performance Art': 'Performance', Family: 'Familie', Circus: 'Cirkus', Magic: 'Magi',
  'Magic & Illusion': 'Magi', Spectacular: 'Show', Electronic: 'Elektronisk',
  Alternative: 'Alternativ', World: 'Verdensmusik', Folk: 'Folkemusik', Religious: 'Religiøs musik',
  Miscellaneous: 'Diverse', Lectures: 'Foredrag', 'Lecture/Seminar': 'Foredrag',
};

export const genreName = (genre: string) => genres[genre] ?? genre;

/** A topic's name for people: categories and genres in Danish, performers and venues as they are. */
export function topicName(t: Pick<Topic, 'kind' | 'name'>) {
  if (t.kind === 'Category') return categoryNames[t.name as Category] ?? t.name;
  if (t.kind === 'Genre') return genreName(t.name);
  return t.name;
}
