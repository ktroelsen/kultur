import { useCallback, useEffect, useState } from 'react';
import { errorMessage, fetchGroups, setChoice, type EventGroup, type GroupView, type SeriesChoice } from './api';

// A grouped list ("Siden sidst" or "Til dig"). Choosing or turning down a group takes it off the list
// at once; it comes back if the server says no.
export function useGroups(view: GroupView) {
  const [groups, setGroups] = useState<EventGroup[] | null>(null);
  const [lastVisit, setLastVisit] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  // The group just turned down, for "Fortryd".
  const [removed, setRemoved] = useState<{ group: EventGroup; all: boolean } | null>(null);

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
    const group = groups?.find((g) => g.key === key) ?? null;
    setGroups((list) => list?.filter((g) => g.key !== key) ?? null);
    try {
      await setChoice(key, choice);
      setRemoved(group && (choice === 'Dismissed' || choice === 'Declined') ? { group, all: choice === 'Dismissed' } : null);
    } catch (err) {
      setGroups(before);
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }, [groups]);

  // Brings the group just turned down back (all its dates).
  const undo = useCallback(async () => {
    if (!removed) return;
    setRemoved(null);
    try {
      await setChoice(removed.group.key, null);
      await load();
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke fortryde.'));
    }
  }, [removed, load]);

  // The offer to undo goes away after a while.
  useEffect(() => {
    if (!removed) return;
    const timer = setTimeout(() => setRemoved(null), 8000);
    return () => clearTimeout(timer);
  }, [removed]);

  return { groups, lastVisit, error, choose, removed, undo, reload: load };
}
