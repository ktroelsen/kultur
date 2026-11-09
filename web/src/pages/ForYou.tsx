import { useState } from 'react';
import { categoryNames, zoneNames, type Category, type Zone } from '../api';
import EventList from '../components/EventList';
import { useEvents } from '../useEvents';

const zones: Zone[] = ['Copenhagen', 'Denmark', 'Abroad'];

// Everything not turned down, nearest first. Claude will rank this against the profile later.
export default function ForYou() {
  const { items, error, react } = useEvents('forYou');
  const [category, setCategory] = useState<Category | null>(null);

  const shown = items?.filter((i) => category === null || i.event.category === category) ?? [];
  const categories = [...new Set(items?.map((i) => i.event.category))];

  return (
    <section>
      <h1 className="text-2xl font-semibold">Til dig</h1>

      {categories.length > 1 && (
        <div className="mt-4 flex flex-wrap gap-2">
          {[null, ...categories].map((c) => (
            <button
              key={c ?? 'all'}
              onClick={() => setCategory(c)}
              className={`rounded-full border px-3 py-1.5 text-sm transition ${
                category === c ? 'border-accent bg-accent/15 text-accent' : 'border-line text-muted hover:text-fg'
              }`}
            >
              {c ? categoryNames[c] : 'Alt'}
            </button>
          ))}
        </div>
      )}

      {error && <p className="mt-6 text-danger">{error}</p>}
      {items === null && !error && <p className="mt-6 text-muted">Henter…</p>}
      {items !== null && shown.length === 0 && (
        <div className="mt-6 rounded-2xl bg-surface p-6 text-muted">Ingen arrangementer endnu.</div>
      )}

      {zones.map((zone) => {
        const inZone = shown.filter((i) => i.zone === zone);
        if (inZone.length === 0) return null;
        return (
          <div key={zone} className="mt-8">
            <h2 className="mb-3 text-sm font-medium tracking-wide text-muted uppercase">{zoneNames[zone]}</h2>
            <EventList items={inZone} onReact={react} />
          </div>
        );
      })}
    </section>
  );
}
