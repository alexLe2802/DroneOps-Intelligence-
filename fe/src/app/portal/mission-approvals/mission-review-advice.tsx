"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/auth/client";

type Review = {
  missionId: string;
  advice: string;
  riskLevel: string;
  risks: string[];
  recommendation: string;
};

export default function MissionReviewAdvice({ missionId }: { missionId: string }) {
  const [review, setReview] = useState<Review | null>(null);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    void api<Review>(`/ai/mission-review/${encodeURIComponent(missionId)}`, {
      method: "POST",
      signal: controller.signal,
    }).then(result => {
      if (controller.signal.aborted) return;
      if (result.missionId.toLowerCase() !== missionId.toLowerCase()) throw new Error("Mission mismatch");
      setReview(result);
    }).catch(() => {
      if (!controller.signal.aborted) setFailed(true);
    });
    return () => controller.abort();
  }, [missionId, attempt]);

  return <section className="approval-request-context" aria-label="AI mission review">
    <strong>AI mission review</strong>
    {review ? <>
      <p>Risk level · {review.riskLevel || "Unavailable"}</p>
      <p>{review.advice}</p>
      {review.risks.length > 0 && <ul>{review.risks.map((risk, index) => <li key={index}>{risk}</li>)}</ul>}
      {review.recommendation && <p><b>Recommendation</b> · {review.recommendation}</p>}
    </> : failed ? <>
      <p role="status">AI review unavailable. You can continue reviewing manually.</p>
      <button type="button" className="module-secondary" onClick={() => { setFailed(false); setAttempt(value => value + 1); }}>Retry AI review</button>
    </> : <p role="status">Preparing AI review…</p>}
  </section>;
}
