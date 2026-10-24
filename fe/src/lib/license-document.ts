export const LICENSE_ACCEPT = ".pdf,.doc,.docx,application/pdf,application/msword,application/vnd.openxmlformats-officedocument.wordprocessingml.document";
export function isLicenseDocument(file: File): boolean {
  const types: Record<string, string> = { pdf: "application/pdf", doc: "application/msword", docx: "application/vnd.openxmlformats-officedocument.wordprocessingml.document" };
  const extension = file.name.split(".").pop()?.toLowerCase() ?? "";
  return Boolean(types[extension] && file.type === types[extension]);
}
export function licenseDocumentLabel(url: string): string | null {
  const extension = new URL(url).pathname.split(".").pop()?.toLowerCase();
  return extension === "pdf" ? "PDF" : extension === "doc" || extension === "docx" ? "Word" : null;
}
