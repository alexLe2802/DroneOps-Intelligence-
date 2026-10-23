"use client";

import { useRef, useState } from "react";
import { LICENSE_ACCEPT, isLicenseDocument } from "@/lib/license-document";

const fileKey = (file: File) => `${file.name}-${file.size}-${file.lastModified}`;
const formatSize = (bytes: number) => bytes < 1024 * 1024
  ? `${Math.max(1, Math.round(bytes / 1024))} KB`
  : `${(bytes / 1024 / 1024).toFixed(1)} MB`;

export default function LicenseFilePicker() {
  const inputRef = useRef<HTMLInputElement>(null);
  const [files, setFiles] = useState<File[]>([]);
  const inputId = "pilot-license-files";

  function updateInput(nextFiles: File[]) {
    if (inputRef.current && nextFiles.length === 0) inputRef.current.setCustomValidity("");
    const transfer = new DataTransfer();
    nextFiles.forEach(file => transfer.items.add(file));
    if (inputRef.current) inputRef.current.files = transfer.files;
    setFiles(nextFiles);
  }

  return <div className="pilot-register-wide pilot-license-upload">
    <label htmlFor={inputId}>Flight license document</label>
    <input
      id={inputId}
      ref={inputRef}
      required
      name="licenseFiles"
      type="file"
      accept={LICENSE_ACCEPT}
      onChange={event => {
        const selected = Array.from(event.currentTarget.files ?? []);
        const valid = selected.length === 0 || (selected.length === 1 && isLicenseDocument(selected[0]) && selected[0].size <= 10 * 1024 * 1024);
        event.currentTarget.setCustomValidity(valid ? "" : "Attach exactly one PDF or Word file, up to 10 MB.");
        updateInput(selected);
        if (!valid) event.currentTarget.reportValidity();
      }}
    />
    <small>Vui lòng scan đầy đủ các trang giấy phép và gộp thành 1 file duy nhất trước khi đính kèm. Chỉ nhận PDF hoặc Word (.doc, .docx), tối đa 10 MB.</small>
    {files.length > 0 && <div className="license-file-selection" aria-label="Selected flight license files">
      <header><b>{files.length} file{files.length === 1 ? "" : "s"} attached</b><button type="button" onClick={() => updateInput([])}>Clear all</button></header>
      <ul>{files.map(file => <li key={fileKey(file)}>
        <span className={file.type === "application/pdf" ? "pdf" : "image"}>{file.type === "application/pdf" ? "PDF" : "WORD"}</span>
        <div><b>{file.name}</b><small>{formatSize(file.size)}</small></div>
        <button type="button" aria-label={`Remove ${file.name}`} onClick={() => updateInput(files.filter(item => fileKey(item) !== fileKey(file)))}>Remove</button>
      </li>)}</ul>
    </div>}
  </div>;
}
