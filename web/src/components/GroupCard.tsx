import { ChevronRight, Heart, Plus, Ticket, X } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { categoryNames, interestNames, type EventGroup, type SeriesChoice } from '../api';
import { cityList, dateRange, genreName, groupPrice, topicName } from '../format';

const saleFormat = new Intl.DateTimeFormat('da-DK', { day: 'numeric', month: 'short' });

/** One event with all its dates. "Siden sidst": Vælg til / Nej tak. "Til dig": take it off again. */
export default function GroupCard({
  group: g,
  onChoice,
}: {
  group: EventGroup;
  onChoice: (choice: SeriesChoice | null) => void;
}) {
  const [confirmNo, setConfirmNo] = useState(false);
  const cost = groupPrice(g);
  const cities = cityList(g.cities);
  const performers = g.performers?.filter((p) => !g.title.toLowerCase().includes(p.toLowerCase())) ?? [];
  const sale = g.saleStart ? saleFormat.format(new Date(g.saleStart)) : null;

  return (
    <article className="overflow-hidden rounded-2xl bg-surface">
      <Link to={`/group?key=${encodeURIComponent(g.key)}`} className="block p-4 pb-2 transition hover:bg-surface-2/50">
        {g.imageUrl && <img src={g.imageUrl} alt="" loading="lazy" className="-mx-4 -mt-4 mb-3 aspect-[16/7] w-[calc(100%+2rem)] max-w-none object-cover" />}
        <p className="flex items-center gap-2 text-xs font-medium tracking-wide text-accent uppercase">
          <span>
            {categoryNames[g.category]}
            {g.genre && <span className="text-muted"> · {genreName(g.genre)}</span>}
          </span>
          {g.isNew && !g.chosen && <span className="rounded bg-accent px-1.5 py-0.5 text-[10px] text-on-accent">Ny</span>}
        </p>
        <h3 className="mt-1 text-lg leading-snug font-semibold">{g.title}</h3>
        {performers.length > 0 && <p className="text-sm text-muted">{performers.slice(0, 3).join(', ')}</p>}
        <p className="mt-1 text-sm">
          {dateRange(g)}
          <span className="text-muted"> · {g.dates === 1 ? '1 dato' : `${g.dates} datoer`}</span>
        </p>
        <p className="text-sm text-muted">
          {cities}
          {cost && <span>{cities ? ' · ' : ''}{cost}</span>}
        </p>
        {g.note && <p className="mt-2 text-sm">{g.note}</p>}
        {g.because && g.becauseRating && (
          <p className="mt-2 flex items-center gap-1.5 text-sm text-muted">
            <Heart className="h-4 w-4 text-accent" />
            Du sagde “{interestNames[g.becauseRating].toLowerCase()}” til {topicName(g.because)}
          </p>
        )}
        {sale && (
          <p className="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-accent/15 px-2 py-1 text-sm font-medium text-accent">
            <Ticket className="h-4 w-4" />
            {g.category === 'Running' ? 'Tilmelding åbner' : 'Billetsalg starter'} {sale}
          </p>
        )}
        <p className="mt-2 flex items-center gap-0.5 text-sm text-accent">
          Se alle datoer <ChevronRight className="h-4 w-4" />
        </p>
      </Link>

      <div className="flex flex-wrap items-center gap-2 px-4 pt-1 pb-4">
        {g.chosen ? (
          <button
            onClick={() => onChoice(null)}
            className="flex items-center gap-1.5 rounded-full border border-line px-3 py-1.5 text-sm text-muted hover:text-fg"
          >
            <X className="h-4 w-4" />
            Fjern fra Til dig
          </button>
        ) : confirmNo ? (
          <>
            <span className="text-sm text-muted">Nej tak til alle datoer?</span>
            <button
              onClick={() => onChoice('Dismissed')}
              className="rounded-full border border-line px-3 py-1.5 text-sm hover:border-accent hover:text-accent"
            >
              Ja, fjern
            </button>
            <button onClick={() => setConfirmNo(false)} className="px-2 py-1.5 text-sm text-muted hover:text-fg">
              Fortryd
            </button>
          </>
        ) : (
          <>
            <button
              onClick={() => onChoice('Chosen')}
              className="flex items-center gap-1.5 rounded-full bg-accent px-3 py-1.5 text-sm font-medium text-on-accent hover:bg-accent-strong"
            >
              <Plus className="h-4 w-4" />
              Vælg til
            </button>
            <button
              onClick={() => setConfirmNo(true)}
              className="flex items-center gap-1.5 rounded-full border border-line px-3 py-1.5 text-sm text-muted hover:text-fg"
            >
              <X className="h-4 w-4" />
              Nej tak
            </button>
          </>
        )}
      </div>
    </article>
  );
}
