"use client";

import Image from "next/image";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { getAssignedUav } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import DeleteUavButton from "../delete-uav-button";
import UavInformation from "../uav-information";

export default function UavDetailClient({ id }: { id: string }) {
  const searchParams = useSearchParams();
  const [aircraft, setAircraft] = useState<AssignedUav | null | undefined>();
  const [error, setError] = useState<unknown>(null);
  const load = useCallback(() => {
    setAircraft(undefined);
    setError(null);
    void getAssignedUav(id).then(setAircraft).catch(setError);
  }, [id]);
  useEffect(() => {
    let active = true;
    void getAssignedUav(id)
      .then(value => { if (active) setAircraft(value); })
      .catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [id]);
  useEffect(() => {
    const pending = aircraft?.imageModerationStatus;
    if (!pending || !["Pending", "Processing", "RetryScheduled"].includes(pending)) return;
    const timer = window.setInterval(() => {
      void getAssignedUav(id).then(setAircraft).catch(setError);
    }, 5000);
    return () => window.clearInterval(timer);
  }, [aircraft?.imageModerationStatus, id]);

  if (error) return <ModuleError error={error} retry={load} />;
  if (aircraft === undefined) return <ModuleLoading label="Loading UAV details" />;
  if (aircraft === null) return <section className="module-state error">
    <span className="state-symbol">!</span><p className="module-kicker">HTTP 404</p><h2>UAV not found</h2>
    <p>This aircraft does not exist or was removed.</p>
    <Link className="module-primary module-link" href="/portal/uav">BACK TO UAV LIST</Link>
  </section>;

  const submitted = searchParams.get("submitted");
  const imageUpload = searchParams.get("imageUpload");
  const normalizedStatus = aircraft.status.toLowerCase();
  const approvalLabel = normalizedStatus === "active"
    ? "ADMIN APPROVED"
    : normalizedStatus === "pending"
      ? "AWAITING APPROVAL"
      : normalizedStatus === "rejected"
        ? "REGISTRATION REJECTED"
        : "NOT OPERATIONAL";

  return <>
    <header className="module-heading">
      <div><p className="module-kicker">PILOT / UAV DETAILS</p><h1>{aircraft.code}</h1><p>Registration and operational status from the backend registry.</p></div>
      <div className="heading-actions"><Link className="module-secondary module-link" href="/portal/uav">← ALL UAV</Link></div>
    </header>

    <section className="uav-detail">
      <div className={`uav-visual${aircraft.imageUrl ? " has-image" : ""}`}>
        {aircraft.imageUrl
          ? <Image src={aircraft.imageUrl} alt={`${aircraft.name} UAV`} width={720} height={720} unoptimized />
          : <><span>UAV</span><b>{aircraft.code}</b></>}
      </div>
      <div className="uav-detail-info">
        <header><span className={`status status-${normalizedStatus}`}>{aircraft.status}</span><small className={normalizedStatus === "active" ? "approval-approved" : "approval-pending"}>{approvalLabel}</small></header>
        <h2>{aircraft.name}</h2><p>{aircraft.model ?? "Model not specified"}</p>
        <dl><div><dt>Code</dt><dd>{aircraft.code}</dd></div><div><dt>Name</dt><dd>{aircraft.name}</dd></div><div><dt>Model</dt><dd>{aircraft.model ?? "Not specified"}</dd></div><div><dt>Status</dt><dd>{aircraft.status}</dd></div><div><dt>Image check</dt><dd>{aircraft.imageModerationStatus ?? "No image"}</dd></div><div><dt>Created at</dt><dd>{aircraft.createdAt.slice(0, 10)}</dd></div><div><dt>Pilot</dt><dd>{aircraft.pilotName || "Current pilot"}</dd></div></dl>
        {submitted && <p className="approval-banner" role="status" aria-live="polite">{submitted === "create" ? "UAV registration submitted. Waiting for manager approval." : "UAV changes saved successfully."}</p>}
        {imageUpload === "success" && <p className="module-success uav-image-feedback" role="status">UAV image uploaded and queued for AI review. This page refreshes the result automatically.</p>}
        {aircraft.imageModerationStatus === "ManualReview" && <p className="approval-banner" role="status">AI is temporarily unavailable. The image is waiting for manager review and was not rejected.</p>}
        {aircraft.imageModerationStatus === "Rejected" && <p className="form-error uav-image-feedback" role="alert">AI could not confirm this as a safe UAV image. Replace it or ask a manager to review it.</p>}
        {imageUpload === "failed" && <p className="form-error uav-image-feedback" role="alert">The UAV was saved, but its image could not be uploaded. The previous image was kept. Edit the UAV to try again.</p>}
        <div className="uav-actions"><Link className="module-secondary module-link" href={`/portal/uav/${aircraft.id}/edit`}>EDIT UAV</Link><DeleteUavButton id={aircraft.id} code={aircraft.code} /></div>
      </div>
    </section>
    <UavInformation aircraft={aircraft} />
  </>;
}
