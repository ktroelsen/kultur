import { Bookmark, CircleHelp, ExternalLink, Ticket, X } from 'lucide-react';
import { categoryNames, type EventItem, type Reaction } from '../api';
import { price, upcomingSale, when, where } from '../format';

const choices: { reaction: Reaction; label: string; icon: typeof Bookmark }[] = [
  { reaction: 'Saved', label: 'Gem', icon: Bookmark },
  { reaction: 'Maybe', label: 'Måske', icon: CircleHelp },
  { reaction: 'NotInterested', label: 'Nej tak', icon: X },
];

export default function EventCard({ item, onReact }: { item: EventItem; onReact: (reaction: Reaction | null) => void }) {
  const { event: e, reaction } = item;
  const sale = upcomingSale(e);
  const cost = price(e);

  return (
    <article className={`overflow-hidden rounded-2xl bg-surface transition ${reaction === 'NotInterested' ? 'opacity-50' : ''}`}>
      {e.imageUrl && <img src={e.imageUrl} alt="" loading="lazy" className="aspect-[16/7] w-full object-cover" />}
      <div className="p-4">
        <p className="text-xs font-medium tracking-wide text-accent uppercase">
          {categoryNames[e.category]}
          {e.genre && <span className="text-muted"> · {e.genre}</span>}
        </p>
        <h3 className="mt-1 text-lg leading-snug font-semibold">{e.title}</h3>
        <p className="mt-1 text-sm text-muted">{when(e)}</p>
        <p className="text-sm text-muted">
          {where(e)}
          {cost && <span> · {cost}</span>}
        </p>
        {sale && (
          <p className="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-accent/15 px-2 py-1 text-sm font-medium text-accent">
            <Ticket className="h-4 w-4" />
            Billetsalg starter {sale}
          </p>
        )}

        <div className="mt-4 flex flex-wrap items-center gap-2">
          {choices.map(({ reaction: r, label, icon: Icon }) => {
            const active = reaction === r;
            return (
              <button
                key={r}
                onClick={() => onReact(active ? null : r)}
                aria-pressed={active}
                className={`flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition ${
                  active ? 'border-accent bg-accent text-on-accent' : 'border-line text-muted hover:text-fg'
                }`}
              >
                <Icon className="h-4 w-4" />
                {label}
              </button>
            );
          })}
          <a
            href={e.url}
            target="_blank"
            rel="noreferrer"
            className="ml-auto flex items-center gap-1 text-sm text-accent hover:underline"
          >
            Billetter
            <ExternalLink className="h-3.5 w-3.5" />
          </a>
        </div>
      </div>
    </article>
  );
}
