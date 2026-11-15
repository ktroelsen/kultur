import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';
import { useLocation, useNavigationType } from 'react-router';

// Where each page was scrolled to, for as long as the app is open.
const positions = new Map<string, number>();

/**
 * The pages scroll inside the layout's <main>, not the window, so the browser can't restore the
 * position by itself. Going back (POP) returns to where you were – waiting a moment if the list is
 * still filling in; following a link (PUSH) starts at the top.
 */
export function useScrollMemory(ref: RefObject<HTMLElement | null>) {
  const location = useLocation();
  const navigationType = useNavigationType();
  const key = location.pathname + location.search;
  const keyRef = useRef(key);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const onScroll = () => positions.set(keyRef.current, el.scrollTop);
    el.addEventListener('scroll', onScroll, { passive: true });
    return () => el.removeEventListener('scroll', onScroll);
  }, [ref]);

  useLayoutEffect(() => {
    keyRef.current = key;
    const el = ref.current;
    if (!el) return;
    const target = navigationType === 'POP' ? (positions.get(key) ?? 0) : 0;
    el.scrollTop = target;
    if (target === 0) return;

    // The list may still be loading: keep trying for a second, until it's tall enough.
    let frame = 0;
    let tries = 0;
    const retry = () => {
      if (el.scrollTop >= target - 1 || tries++ > 60) return;
      el.scrollTop = target;
      frame = requestAnimationFrame(retry);
    };
    frame = requestAnimationFrame(retry);
    return () => cancelAnimationFrame(frame);
  }, [key, navigationType, ref]);
}
