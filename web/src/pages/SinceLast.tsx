import { useEffect, useRef } from 'react';
import { api } from '../api';
import EventList from '../components/EventList';
import { useEvents } from '../useEvents';

const visitFormat = new Intl.DateTimeFormat('da-DK', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' });

// The front page: what has turned up since the last visit. Opening it counts as having seen it,
// but the list stays on screen until you leave.
export default function SinceLast() {
  const { items, lastVisit, error, react } = useEvents('new');
  const marked = useRef(false);

  useEffect(() => {
    if (items === null || marked.current) return;
    marked.current = true;
    api('/api/events/seen', { method: 'POST' }).catch(() => {});
  }, [items]);

  return (
    <section>
      <h1 className="text-2xl font-semibold">Siden sidst</h1>
      <p className="mt-1 text-sm text-muted">
        {lastVisit ? `Nyt siden ${visitFormat.format(new Date(lastVisit))}` : 'Alt, jeg har fundet indtil nu'}
      </p>

      {error && <p className="mt-6 text-danger">{error}</p>}
      {items === null && !error && <p className="mt-6 text-muted">Henter…</p>}
      {items?.length === 0 && (
        <div className="mt-6 rounded-2xl bg-surface p-6 text-muted">
          Intet nyt siden sidst. Jeg kigger efter igen i nat.
        </div>
      )}
      {items && items.length > 0 && (
        <div className="mt-6">
          <EventList items={items} onReact={react} />
        </div>
      )}
    </section>
  );
}
