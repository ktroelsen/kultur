/** "Fjernet: X – Fortryd", just above the bottom tab bar. */
export default function UndoBar({ text, onUndo }: { text: string; onUndo: () => void }) {
  return (
    <div className="fixed inset-x-0 bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-10 flex justify-center px-4 md:bottom-6">
      <div className="flex max-w-md items-center gap-3 rounded-xl bg-fg px-4 py-3 text-sm text-ink shadow-lg">
        <span className="min-w-0 flex-1 truncate">{text}</span>
        <button onClick={onUndo} className="shrink-0 font-semibold underline">
          Fortryd
        </button>
      </div>
    </div>
  );
}
