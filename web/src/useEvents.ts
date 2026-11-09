import { useCallback, useEffect, useState } from 'react';
import { errorMessage, fetchEvents, setReaction, type DismissReason, type EventItem, type EventView, type Reaction } from './api';

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

  // "Nej tak – begivenheden/datoen" (or taking one of those back) changes other cards too, so the
  // list is fetched again; otherwise just this card changes.
  const react = useCallback(async (id: string, reaction: Reaction | null, reason: DismissReason | null = null) => {
    const before = items;
    const target = items?.find((i) => i.event.id === id);
    setItems((list) => list?.map((i) => (i.event.id === id ? { ...i, reaction } : i)) ?? null);
    try {
      await setReaction(id, reaction, reason);
      if (reason === 'Event' || reason === 'Date' || target?.reaction === 'NotInterested') {
        const list = await fetchEvents(view);
        setItems(list.events);
      }
    } catch (err) {
      setItems(before);
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }, [items, view]);

  return { items, lastVisit, error, react, reload: load };
}
