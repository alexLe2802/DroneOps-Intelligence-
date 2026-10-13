"use client";

import { useRef, useState } from "react";

const fileKey = (file: File) => `${file.name}-${file.size}-${file.lastModified}`;
const formatSize = (bytes: number) => bytes < 1024 * 1024
  ? `${Math.max(1, Math.round(bytes / 1024))} KB`
  : `${(bytes / 1024 / 1024).toFixed(1)} MB`;

export default function LicenseFilePicker() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<File[]>([]);

  function updateInput(nextFiles: File[]) {
    const transfer = new DataTransfer();
    nextFiles.forEach(file => transfer.items.add(file));
    if (inputRef.current) inputRef.current.files = transfer.files;
    setFiles(nextFiles);
  }

  return <label className="pilot-register-wide pilot-license-upload">
    Flight license pages
    <input
      ref={inputRef}
      required
      multiple
      name="licenseFiles"
      type="file"
      accept="image/png,image/jpeg,image/webp,application/pdf"
      onChange={event => {
        const selected = Array.from(event.currentTarget.files ?? []);
        const merged = [...files, ...selected].filter((file, index, list) => list.findIndex(item => fileKey(item) === fileKey(file)) === index);
        updateInput(merged);
      }}
    />
    <small>Select multiple images or PDF documents · maximum 10 files and 10 MB per file.</small>
    {files.length > 0 && <div className="license-file-selection" aria-label="Selected flight license files">
      <header><b>{files.length} file{files.length === 1 ? "" : "s"} attached</b><button type="button" onClick={() => updateInput([])}>Clear all</button></header>
      <ul>{files.map(file => <li key={fileKey(file)}>
        <span className={file.type === "application/pdf" ? "pdf" : "image"}>{file.type === "application/pdf" ? "PDF" : "IMG"}</span>
        <div><b>{file.name}</b><small>{formatSize(file.size)}</small></div>
        <button type="button" aria-label={`Remove ${file.name}`} onClick={() => updateInput(files.filter(item => fileKey(item) !== fileKey(file)))}>Remove</button>
      </li>)}</ul>
    </div>}
  </label>;
}
