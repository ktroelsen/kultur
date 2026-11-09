import type { DismissReason, EventItem, Reaction } from '../api';
import EventCard from './EventCard';

export default function EventList({ items, onReact }: { items: EventItem[]; onReact: (id: string, r: Reaction | null, reason?: DismissReason | null) => void }) {
  return (
    <div className="space-y-4">
      {items.map((item) => (
        <EventCard key={item.event.id} item={item} onReact={(r, reason) => onReact(item.event.id, r, reason)} />
      ))}
    </div>
  );
}
