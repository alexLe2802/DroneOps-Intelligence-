"use client";

import { useId, useRef, useState } from "react";
import { LICENSE_ACCEPT, isLicenseDocument } from "@/lib/license-document";
import styles from "./license-file-picker.module.css";

const fileKey = (file: File) => `${file.name}-${file.size}-${file.lastModified}`;
const formatSize = (bytes: number) => bytes < 1024 * 1024
  ? `${Math.max(1, Math.round(bytes / 1024))} KB`
  : `${(bytes / 1024 / 1024).toFixed(1)} MB`;

export default function LicenseFilePicker({ allowImages = false }: { allowImages?: boolean }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<File[]>([]);
  const [error, setError] = useState("");
  const inputId = useId();

  function updateInput(nextFiles: File[]) {
    if (inputRef.current && nextFiles.length === 0) inputRef.current.setCustomValidity("");
    const transfer = new DataTransfer();
    nextFiles.forEach(file => transfer.items.add(file));
    if (inputRef.current) inputRef.current.files = transfer.files;
    setFiles(nextFiles);
  }

  return <div className={`${styles.root} pilot-register-wide`}>
    <label className={styles.title} htmlFor={inputId}>Flight license document</label>
    <div className={styles.picker}>
      <input
        className={styles.input}
        id={inputId}
        ref={inputRef}
        required
        name="licenseFiles"
        type="file"
        accept={allowImages ? `${LICENSE_ACCEPT},image/jpeg,image/png,image/webp` : LICENSE_ACCEPT}
        aria-describedby={`${inputId}-help${error ? ` ${inputId}-error` : ""}`}
        onChange={event => {
          const selected = Array.from(event.currentTarget.files ?? []);
          const valid = selected.length === 0 || (selected.length === 1 && (isLicenseDocument(selected[0]) || (allowImages && ["image/jpeg", "image/png", "image/webp"].includes(selected[0].type))) && selected[0].size <= (selected[0].type.startsWith("image/") ? 5 : 10) * 1024 * 1024);
          const validationMessage = allowImages ? "Attach one image up to 5 MB or PDF/Word file up to 10 MB." : "Attach exactly one PDF or Word file, up to 10 MB.";
          event.currentTarget.setCustomValidity(valid ? "" : validationMessage);
          setError(valid ? "" : validationMessage);
          if (valid) updateInput(selected);
          else {
            event.currentTarget.value = "";
            setFiles([]);
            event.currentTarget.reportValidity();
          }
        }}
      />
      <span className={styles.icon} aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 16V4m0 0L7.5 8.5M12 4l4.5 4.5M5 14v4.5A1.5 1.5 0 0 0 6.5 20h11a1.5 1.5 0 0 0 1.5-1.5V14" /></svg></span>
      <span className={styles.copy}><b>{files.length ? "Replace attached file" : "Upload license evidence"}</b><small>Click to browse from your device</small></span>
      <span className={styles.choose}>{files.length ? "Replace file" : "Choose file"}</span>
    </div>
    <small className={styles.help} id={`${inputId}-help`}>{allowImages ? "JPEG, PNG or WebP up to 5 MB. PDF or Word up to 10 MB. Documents remain private to you and authorized reviewers." : "Scan every license page into one PDF or Word file, up to 10 MB."}</small>
    {error && <small className={styles.error} id={`${inputId}-error`} role="alert">{error}</small>}
    {files.length > 0 && <div className={styles.selection} aria-label="Selected flight license files">
      <header><b>{files.length} file{files.length === 1 ? "" : "s"} attached</b><button type="button" onClick={() => { updateInput([]); setError(""); }}>Clear all</button></header>
      <ul>{files.map(file => <li key={fileKey(file)}>
        <span className={file.type === "application/pdf" ? styles.pdf : undefined}>{file.type === "application/pdf" ? "PDF" : file.type.startsWith("image/") ? "IMAGE" : "WORD"}</span>
        <div><b>{file.name}</b><small>{formatSize(file.size)}</small></div>
        <button type="button" aria-label={`Remove ${file.name}`} onClick={() => { updateInput(files.filter(item => fileKey(item) !== fileKey(file))); setError(""); }}>Remove</button>
      </li>)}</ul>
    </div>}
  </div>;
}
