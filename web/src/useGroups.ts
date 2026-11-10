import { useCallback, useEffect, useState } from 'react';
import { errorMessage, fetchGroups, setChoice, type EventGroup, type GroupView, type SeriesChoice } from './api';

// A grouped list ("Siden sidst" or "Til dig"). Choosing or turning down a group takes it off the list
// at once; it comes back if the server says no.
export function useGroups(view: GroupView) {
  const [groups, setGroups] = useState<EventGroup[] | null>(null);
  const [lastVisit, setLastVisit] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const list = await fetchGroups(view);
      setGroups(list.groups);
      setLastVisit(list.lastVisitUtc);
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke hente arrangementerne.'));
    }
  }, [view]);

  useEffect(() => {
    load();
  }, [load]);

  const choose = useCallback(async (key: string, choice: SeriesChoice | null) => {
    const before = groups;
    setGroups((list) => list?.filter((g) => g.key !== key) ?? null);
    try {
      await setChoice(key, choice);
    } catch (err) {
      setGroups(before);
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }, [groups]);

  return { groups, lastVisit, error, choose, reload: load };
}
