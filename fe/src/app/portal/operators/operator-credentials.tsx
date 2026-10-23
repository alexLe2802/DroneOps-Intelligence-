"use client";

import { licenseDocumentLabel } from "@/lib/license-document";
import Image from "next/image";
import { useEffect, useState } from "react";
import { api } from "@/lib/auth/client";
import styles from "./pilot-roster.module.css";
type Credentials = { phoneNumber: string | null; dateOfBirth: string | null; experienceYears: number; pilotLicenseNo: string; licenseIssuedDate: string | null; licenseExpiredDate: string | null; licenseImageUrls: string[]; documentsAvailable: boolean };
export default function OperatorCredentials({ userId }: { userId: string }) {
  const [checkedAt] = useState(() => Date.now());
  const [data, setData] = useState<Credentials | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    void api<Credentials>(`/users/${userId}/credentials`).then(value => { if (active) setData(value); }).catch(e => { if (active) setError(e instanceof Error ? e.message : "Unable to load credentials."); });
    return () => { active = false; };
  }, [userId, revision]);
  const date = (value: string | null) => value ? new Date(`${value}T00:00:00`).toLocaleDateString("en-GB") : "Not provided";
  if (error) return <div className={styles.directive} role="alert"><div><b>Credentials unavailable</b><p>{error}</p><button type="button" className={styles.refresh} onClick={() => { setError(""); setRevision(v => v + 1); }}>Retry</button></div></div>;
  if (!data) return <p className={styles.credentialsLoading} role="status">Loading flight credentials…</p>;
  const expiry = data.licenseExpiredDate ? new Date(`${data.licenseExpiredDate}T23:59:59`) : null;
  const status = !data.pilotLicenseNo ? "License not provided" : !expiry ? "Expiry not provided" : expiry.getTime() < checkedAt ? "Expired" : "Within validity dates";
  return <section className={styles.credentials} aria-label="Current flight credentials"><dl className={styles.fields}>
    {[["Phone number", data.phoneNumber ?? "Not provided"], ["Date of birth", date(data.dateOfBirth)], ["Flight experience", `${data.experienceYears} years`], ["License validity", status], ["License number", data.pilotLicenseNo || "Not provided"], ["License issued", date(data.licenseIssuedDate)], ["License expiry", date(data.licenseExpiredDate)]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}
    </dl><div className={styles.directive}><span aria-hidden="true">◇</span><div><b>Current flight credentials</b><p>Current recorded license details. Pending changes take effect only after manager approval.</p></div></div>
    <h3 className={styles.documentHeading}>License documents</h3>
    {!data.documentsAvailable && <p role="alert">Some documents could not be loaded. Refresh the roster to try again.</p>}
    {data.licenseImageUrls.length ? <div className={styles.documents}>{data.licenseImageUrls.map((url, index) => <a key={url} href={url} target="_blank" rel="noreferrer">{licenseDocumentLabel(url) ? <span>Open license {licenseDocumentLabel(url)} {index + 1}</span> : <Image unoptimized src={url} alt={`Current license document ${index + 1}`} width={600} height={400} style={{ width: "100%", height: "auto" }} />}</a>)}</div> : <p className={styles.credentialsLoading}>No stored documents are available for this license.</p>}
  </section>;
}
