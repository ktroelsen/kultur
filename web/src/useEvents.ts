import { useCallback, useEffect, useState } from 'react';
import { errorMessage, fetchEvents, setReaction, type EventItem, type EventView, type Reaction } from './api';

// One list of events plus Gem / Måske / Nej tak, updated at once and rolled back if the server says no.
export function useEvents(view: EventView) {
  const [items, setItems] = useState<EventItem[] | null>(null);
  const [lastVisit, setLastVisit] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const list = await fetchEvents(view);
      setItems(list.events);
      setLastVisit(list.lastVisitUtc);
      return list;
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke hente arrangementerne.'));
      return null;
    }
  }, [view]);

  useEffect(() => {
    load();
  }, [load]);

  const react = useCallback(async (id: string, reaction: Reaction | null) => {
    const before = items;
    setItems((list) => list?.map((i) => (i.event.id === id ? { ...i, reaction } : i)) ?? null);
    try {
      await setReaction(id, reaction);
    } catch (err) {
      setItems(before);
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }, [items]);

  return { items, lastVisit, error, react, reload: load };
}
