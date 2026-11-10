import { useEffect, useRef } from 'react';
import { api } from '../api';
import GroupList from '../components/GroupList';
import { useGroups } from '../useGroups';

const visitFormat = new Intl.DateTimeFormat('da-DK', { weekday: 'long', day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit' });

// The front page: every suggestion not yet decided on, one entry per event with all its dates – new
// ones first. "Vælg til" moves it to "Til dig". Opening the page counts as having seen what's new.
export default function SinceLast() {
  const { groups, lastVisit, error, choose } = useGroups('inbox');
  const marked = useRef(false);

  useEffect(() => {
    if (groups === null || marked.current) return;
    marked.current = true;
    api('/api/events/seen', { method: 'POST' }).catch(() => {});
  }, [groups]);

  return (
    <section>
      <h1 className="text-2xl font-semibold">Siden sidst</h1>
      <p className="mt-1 text-sm text-muted">
        {lastVisit ? `Nyt siden ${visitFormat.format(new Date(lastVisit))} står øverst.` : 'Alt, jeg har fundet indtil nu.'}{' '}
        Vælg det til, du har lyst til.
      </p>

      {error && <p className="mt-6 text-danger">{error}</p>}
      {groups === null && !error && <p className="mt-6 text-muted">Henter…</p>}
      {groups && (
        <GroupList
          groups={groups}
          onChoice={choose}
          empty="Intet nyt at tage stilling til. Jeg kigger efter igen i nat."
          sections={[
            { title: 'Nyt', filter: (g) => g.isNew },
            { title: 'Forslag', filter: (g) => !g.isNew },
          ]}
        />
      )}
    </section>
  );
}
