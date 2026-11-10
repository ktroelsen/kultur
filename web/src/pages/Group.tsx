import { ArrowLeft, Plus, X } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router';
import {
  categoryNames,
  errorMessage,
  fetchGroup,
  setChoice,
  setReaction,
  type DismissReason,
  type GroupDetail,
  type Reaction,
  type SeriesChoice,
} from '../api';
import EventCard from '../components/EventCard';
import { dateRange, genreName } from '../format';

// One event and every one of its dates. Save the date you'll go to, drop the ones you won't – a dropped
// date stays listed (faded) so it can be brought back.
export default function Group() {
  const [params] = useSearchParams();
  const key = params.get('key') ?? '';
  const navigate = useNavigate();
  const [detail, setDetail] = useState<GroupDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setDetail(await fetchGroup(key));
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke hente arrangementet.'));
    }
  }, [key]);

  useEffect(() => {
    load();
  }, [load]);

  async function react(id: string, reaction: Reaction | null, reason?: DismissReason | null) {
    try {
      await setReaction(id, reaction, reason ?? null);
      await load();
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }

  async function choose(choice: SeriesChoice | null) {
    try {
      await setChoice(key, choice);
      await load();
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke gemme dit valg.'));
    }
  }

  const first = detail?.events[0]?.event;
  const g = detail?.group;

  return (
    <section>
      <button onClick={() => navigate(-1)} className="mb-4 flex items-center gap-1.5 text-sm text-muted hover:text-fg">
        <ArrowLeft className="h-4 w-4" /> Tilbage
      </button>

      {error && <p className="text-danger">{error}</p>}
      {!detail && !error && <p className="text-muted">Henter…</p>}

      {detail && first && (
        <>
          <p className="text-xs font-medium tracking-wide text-accent uppercase">
            {categoryNames[first.category]}
            {first.genre && <span className="text-muted"> · {genreName(first.genre)}</span>}
          </p>
          <h1 className="mt-1 text-2xl font-semibold">{g?.title ?? first.title}</h1>
          <p className="mt-1 text-sm text-muted">
            {g ? `${dateRange(g)} · ${g.dates === 1 ? '1 dato' : `${g.dates} datoer`} tilbage` : 'Ingen datoer tilbage'}
            {detail.events.length > (g?.dates ?? 0) && ` af ${detail.events.length}`}
          </p>

          <div className="mt-4 flex flex-wrap gap-2">
            {detail.chosen ? (
              <button onClick={() => choose(null)} className="flex items-center gap-1.5 rounded-full border border-line px-3 py-1.5 text-sm text-muted hover:text-fg">
                <X className="h-4 w-4" /> Fjern fra Til dig
              </button>
            ) : detail.dismissed ? (
              <button onClick={() => choose(null)} className="rounded-full border border-line px-3 py-1.5 text-sm text-muted hover:text-fg">
                Fortryd nej tak
              </button>
            ) : (
              <>
                <button onClick={() => choose('Chosen')} className="flex items-center gap-1.5 rounded-full bg-accent px-3 py-1.5 text-sm font-medium text-on-accent hover:bg-accent-strong">
                  <Plus className="h-4 w-4" /> Vælg til
                </button>
                <button onClick={() => choose('Dismissed')} className="flex items-center gap-1.5 rounded-full border border-line px-3 py-1.5 text-sm text-muted hover:text-fg">
                  <X className="h-4 w-4" /> Nej tak til alle datoer
                </button>
              </>
            )}
          </div>

          <h2 className="mt-8 mb-3 text-sm font-medium tracking-wide text-muted uppercase">Datoer</h2>
          <div className="space-y-4">
            {detail.events.map((item) => (
              <EventCard key={item.event.id} item={item} onReact={(r, reason) => react(item.event.id, r, reason)} />
            ))}
          </div>
        </>
      )}
    </section>
  );
}
