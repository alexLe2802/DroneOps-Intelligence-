import { isLicenseDocument } from "./license-document";

export function validateLicenseChange(number: string, issued: string, expires: string, files: File[], now = new Date()): string | null {
  if (!number.trim()) return "License number is required.";
  const issueDate = new Date(`${issued}T00:00:00`);
  const expiryDate = new Date(`${expires}T00:00:00`);
  const today = new Date(now); today.setHours(0, 0, 0, 0);
  if (!Number.isFinite(issueDate.getTime()) || issueDate > today) return "Enter a valid issue date that is not in the future.";
  if (!Number.isFinite(expiryDate.getTime()) || expiryDate <= today) return "Expiration must be in the future.";
  if (expiryDate <= issueDate) return "Expiration must be after the issue date.";
  if (files.length !== 1 || files.some(file => {
    const image = ["image/jpeg", "image/png", "image/webp"].includes(file.type);
    return file.size <= 0 || !(image || isLicenseDocument(file)) || file.size > (image ? 5 : 10) * 1024 * 1024;
  })) return "Attach one JPEG, PNG or WebP image up to 5 MB, or PDF/Word document up to 10 MB.";
  return null;
}
