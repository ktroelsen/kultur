import { Ban, Bookmark, ExternalLink, Heart, Ticket, X } from 'lucide-react';
import { categoryNames, interestNames, type DismissReason, type EventItem, type Reaction } from '../api';
import { genreName, price, topicName, upcomingSale, when, where } from '../format';

export default function EventCard({
  item,
  onReact,
}: {
  item: EventItem;
  onReact: (reaction: Reaction | null, reason?: DismissReason | null) => void;
}) {
  const { event: e, reaction, because, becauseRating } = item;
  const sale = upcomingSale(e);
  const cost = price(e);

  return (
    <article className={`overflow-hidden rounded-2xl bg-surface transition ${reaction === 'NotInterested' ? 'opacity-50' : ''}`}>
      {e.imageUrl && <img src={e.imageUrl} alt="" loading="lazy" className="aspect-[16/7] w-full object-cover" />}
      <div className="p-4">
        <p className="text-xs font-medium tracking-wide text-accent uppercase">
          {categoryNames[e.category]}
          {e.genre && <span className="text-muted"> · {genreName(e.genre)}</span>}
        </p>
        <h3 className="mt-1 text-lg leading-snug font-semibold">{e.title}</h3>
        <p className="mt-1 text-sm text-muted">{when(e)}</p>
        <p className="text-sm text-muted">
          {where(e)}
          {cost && <span> · {cost}</span>}
        </p>
        {e.note && <p className="mt-2 text-sm">{e.note}</p>}
        {because && becauseRating && (
          <p className="mt-2 flex items-center gap-1.5 text-sm text-muted">
            <Heart className="h-4 w-4 text-accent" />
            Du sagde “{interestNames[becauseRating].toLowerCase()}” til {topicName(because)}
          </p>
        )}
        {sale && (
          <p className="mt-2 inline-flex items-center gap-1.5 rounded-lg bg-accent/15 px-2 py-1 text-sm font-medium text-accent">
            <Ticket className="h-4 w-4" />
            {e.category === 'Running' ? 'Tilmelding åbner' : 'Billetsalg starter'} {sale}
          </p>
        )}

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Choice active={reaction === 'Saved'} icon={Bookmark} label="Gem" onClick={() => onReact(reaction === 'Saved' ? null : 'Saved')} />
          {reaction === 'NotInterested' ? (
            <Choice active icon={X} label="Nej tak · fortryd" onClick={() => onReact(null)} />
          ) : (
            <>
              <Choice icon={X} label="Nej tak" title="Kun denne dato" onClick={() => onReact('NotInterested')} />
              <Choice
                icon={Ban}
                label="Nej tak alle"
                title="Arrangementet uanset dato og sted – også fremtidige datoer"
                onClick={() => onReact('NotInterested', 'Event')}
              />
            </>
          )}
          <a
            href={e.url}
            target="_blank"
            rel="noreferrer"
            className="ml-auto flex items-center gap-1 text-sm text-accent hover:underline"
          >
            {e.category === 'Running' ? 'Løbets side' : 'Billetter'}
            <ExternalLink className="h-3.5 w-3.5" />
          </a>
        </div>
        {reaction !== 'NotInterested' && e.date && (
          <button onClick={() => onReact('NotInterested', 'Date')} className="mt-2 text-xs text-muted underline hover:text-fg">
            Optaget den dag – skjul alt den dato
          </button>
        )}
      </div>
    </article>
  );
}

function Choice({
  active = false,
  icon: Icon,
  label,
  title,
  onClick,
}: {
  active?: boolean;
  icon: typeof X;
  label: string;
  title?: string;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      title={title}
      aria-pressed={active}
      className={`flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition ${
        active ? 'border-accent bg-accent text-on-accent' : 'border-line text-muted hover:text-fg'
      }`}
    >
      <Icon className="h-4 w-4" />
      {label}
    </button>
  );
}
