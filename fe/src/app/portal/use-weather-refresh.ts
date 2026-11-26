"use client";

import { useEffect, useState } from "react";

// Re-enable refresh at the server deadline without polling the weather API.
export function useWeatherRefreshWait(deadline: number) {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    if (!deadline) return;
    const tick = () => setNow(Date.now());
    const timer = window.setInterval(() => {
      tick();
      if (Date.now() >= deadline) window.clearInterval(timer);
    }, 1000);
    tick();
    return () => window.clearInterval(timer);
  }, [deadline]);
  return Math.max(0, Math.ceil((deadline - now) / 1000));
}
