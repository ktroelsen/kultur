import type { CultureEvent } from './api';

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
