import EventList from '../components/EventList';
import { useEvents } from '../useEvents';

export default function Saved() {
  const { items, error, react } = useEvents('saved');

  return (
    <section>
      <h1 className="text-2xl font-semibold">Gemt</h1>
      <p className="mt-1 text-sm text-muted">De datoer, du har gemt.</p>

      {error && <p className="mt-6 text-danger">{error}</p>}
      {items === null && !error && <p className="mt-6 text-muted">Henter…</p>}
      {items?.length === 0 && (
        <div className="mt-6 rounded-2xl bg-surface p-6 text-muted">Du har ikke gemt noget endnu.</div>
      )}
      {items && items.length > 0 && (
        <div className="mt-6">
          <EventList items={items} onReact={react} />
        </div>
      )}
    </section>
  );
}
