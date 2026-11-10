import GroupList from '../components/GroupList';
import UndoBar from '../components/UndoBar';
import { useGroups } from '../useGroups';

// What you've chosen from "Siden sidst", by date. Open one to see its dates and drop the ones you don't want.
export default function ForYou() {
  const { groups, error, choose, removed, undo } = useGroups('chosen');

  return (
    <section>
      <h1 className="text-2xl font-semibold">Til dig</h1>
      <p className="mt-1 text-sm text-muted">Det, du har valgt til. Åbn et for at se datoerne og fjerne dem, du ikke vil have.</p>

      {error && <p className="mt-6 text-danger">{error}</p>}
      {groups === null && !error && <p className="mt-6 text-muted">Henter…</p>}
      {groups && (
        <GroupList groups={groups} onChoice={choose} empty="Du har ikke valgt noget til endnu. Tryk “Vælg til” under Siden sidst." />
      )}
      {removed && <UndoBar text={`Fjernet: ${removed.title} – alle datoer`} onUndo={undo} />}
    </section>
  );
}
