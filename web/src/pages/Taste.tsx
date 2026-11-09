import { ChevronDown, X } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import {
  api,
  errorMessage,
  interestNames,
  interestScale,
  rateTopic,
  topicKindNames,
  type Interest,
  type NextTopic,
  type RatedTopic,
} from '../api';
import { topicName } from '../format';

const dayFormat = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short' });

function shortDate(date: string | null) {
  if (!date) return null;
  const [y, m, d] = date.split('-').map(Number);
  return dayFormat.format(new Date(y, m - 1, d));
}

// "Smag": one topic at a time – a category, genre, artist/team or venue – rated from "aldrig" to
// "meget". Come back now and then; the ratings decide what "Til dig" shows and in which order.
export default function Taste() {
  const [next, setNext] = useState<NextTopic | null | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [rated, setRated] = useState<RatedTopic[] | null>(null);
  const [showRated, setShowRated] = useState(false);

  const loadNext = useCallback(async () => {
    setError(null);
    try {
      setNext((await api<NextTopic | undefined>('/api/taste/next')) ?? null);
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke hente næste forslag.'));
    }
  }, []);

  const loadRated = useCallback(async () => {
    try {
      setRated(await api<RatedTopic[]>('/api/taste'));
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke hente dine vurderinger.'));
    }
  }, []);

  useEffect(() => {
    loadNext();
  }, [loadNext]);

  useEffect(() => {
    if (showRated) loadRated();
  }, [showRated, loadRated]);

  async function rate(rating: Interest | null) {
    if (!next) return;
    setBusy(true);
    try {
      await rateTopic(next.topic.kind, next.topic.name, rating);
      await loadNext();
      if (showRated) await loadRated();
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke gemme din vurdering.'));
    } finally {
      setBusy(false);
    }
  }

  async function change(t: RatedTopic, rating: Interest | 'remove') {
    try {
      if (rating === 'remove') await api(`/api/taste/${encodeURIComponent(t.key)}`, { method: 'DELETE' });
      else await rateTopic(t.kind, t.name, rating);
      await loadRated();
      await loadNext();
    } catch (err) {
      setError(errorMessage(err, 'Kunne ikke ændre vurderingen.'));
    }
  }

  return (
    <section>
      <h1 className="text-2xl font-semibold">Smag</h1>
      <p className="mt-1 text-sm text-muted">
        Hvor interesseret er du? Tag et par stykker ad gangen – jo flere, jo bedre bliver “Til dig”.
      </p>

      {error && <p className="mt-6 text-danger">{error}</p>}
      {next === undefined && !error && <p className="mt-6 text-muted">Henter…</p>}
      {next === null && (
        <div className="mt-6 rounded-2xl bg-surface p-6 text-muted">
          Du har taget stilling til alt, jeg kender lige nu. Kom tilbage, når der er kommet nye arrangementer.
        </div>
      )}

      {next && (
        <div className="mt-6 rounded-2xl bg-surface p-5">
          <p className="text-xs font-medium tracking-wide text-accent uppercase">{topicKindNames[next.topic.kind]}</p>
          <h2 className="mt-1 text-2xl font-semibold">{topicName(next.topic)}</h2>
          <p className="mt-1 text-sm text-muted">
            {next.upcoming === 1 ? '1 kommende arrangement' : `${next.upcoming} kommende arrangementer`}
          </p>
          <ul className="mt-3 space-y-1 text-sm">
            {next.examples.map((e, i) => (
              <li key={i} className="truncate">
                {e.title}
                <span className="text-muted">
                  {[shortDate(e.date), e.venue].filter(Boolean).map((x) => ` · ${x}`)}
                </span>
              </li>
            ))}
          </ul>

          <div className="mt-5 grid grid-cols-5 gap-1.5">
            {interestScale.map(({ value, label }, i) => (
              <button
                key={value}
                disabled={busy}
                onClick={() => rate(value)}
                className="rounded-lg border border-line py-3 text-sm transition hover:border-accent hover:text-accent disabled:opacity-50"
                style={{ backgroundColor: `color-mix(in srgb, var(--color-accent) ${i * 7}%, transparent)` }}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="mt-3 flex items-center justify-between text-sm text-muted">
            <button disabled={busy} onClick={() => rate(null)} className="hover:text-fg disabled:opacity-50">
              Spring over
            </button>
            <span>
              {next.rated} vurderet · {next.left} tilbage
            </span>
          </div>
        </div>
      )}

      <div className="mt-8">
        <button
          onClick={() => setShowRated((v) => !v)}
          className="flex items-center gap-1.5 text-sm font-medium text-muted hover:text-fg"
          aria-expanded={showRated}
        >
          <ChevronDown className={`h-4 w-4 transition ${showRated ? 'rotate-180' : ''}`} />
          Dine vurderinger
        </button>
        {showRated && rated && (
          <ul className="mt-3 divide-y divide-line rounded-2xl bg-surface">
            {rated.length === 0 && <li className="p-4 text-sm text-muted">Ingen endnu.</li>}
            {rated.map((t) => (
              <li key={t.key} className="flex items-center gap-3 px-4 py-2.5">
                <div className="min-w-0 flex-1">
                  <p className="truncate">{topicName(t)}</p>
                  <p className="text-xs text-muted">{topicKindNames[t.kind]}</p>
                </div>
                <select
                  value={t.rating}
                  onChange={(e) => change(t, e.target.value as Interest)}
                  aria-label={`Vurdering af ${topicName(t)}`}
                  className="rounded-lg border border-line bg-ink px-2 py-1.5 text-sm"
                >
                  {interestScale.map(({ value }) => (
                    <option key={value} value={value}>
                      {interestNames[value]}
                    </option>
                  ))}
                </select>
                <button onClick={() => change(t, 'remove')} aria-label="Fjern" className="p-1 text-muted hover:text-fg">
                  <X className="h-4 w-4" />
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
