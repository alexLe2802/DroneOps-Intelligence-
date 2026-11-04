import { api } from "@/lib/auth/client";

export type PilotRegistrationStatus = "Pending" | "Approved" | "Rejected";

export type CreatePilotRegistrationRequest = {
  fullName: string;
  email: string;
  password: string;
  phoneNumber: string;
  dateOfBirth: string;
  pilotLicenseNo: string;
  licenseIssuedDate: string;
  licenseExpiredDate: string;
  experienceYears: number;
  droneType: string;
  usagePurpose: string;
  description: string | null;
};

export type PilotRegistrationSummary = {
  id: string;
  fullName: string;
  email: string;
  phoneNumber: string | null;
  droneType: string;
  droneModel: string | null;
  pilotLicenseNo: string;
  usagePurpose: string;
  status: PilotRegistrationStatus;
  createdAt: string;
};

export type PilotRegistrationDetail = PilotRegistrationSummary & {
  licenseImageUrls: string[];
  dateOfBirth: string | null;
  licenseIssuedDate: string | null;
  licenseExpiredDate: string | null;
  experienceYears: number | null;
  description: string | null;
  rejectReason: string | null;
  reviewedBy: string | null;
  reviewedAt: string | null;
};

export type PagedPilotRegistrations = {
  items: PilotRegistrationSummary[];
  pageNumber: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
};

export function createPilotRegistration(request: CreatePilotRegistrationRequest, files: File[]) {
  const body = new FormData();
  body.set("data", JSON.stringify(request));
  files.forEach(file => body.append("files", file));
  return api<{ id?: string; status?: PilotRegistrationStatus; message: string }>("/pilot-registrations/with-files", {
    method: "POST",
    body,
  });
}

export function getPilotRegistrations(params: { pageNumber?: number; pageSize?: number; search?: string; status?: string } = {}) {
  const query = new URLSearchParams({
    pageNumber: String(params.pageNumber ?? 1),
    pageSize: String(params.pageSize ?? 20),
  });
  if (params.search) query.set("search", params.search);
  if (params.status && params.status !== "All") query.set("status", params.status);
  return api<PagedPilotRegistrations>(`/pilot-registrations?${query}`);
}

export function getPilotRegistration(id: string) {
  return api<PilotRegistrationDetail>(`/pilot-registrations/${encodeURIComponent(id)}`);
}

export function approvePilotRegistration(id: string, note?: string) {
  return api<{ success: boolean; message: string }>(`/pilot-registrations/${encodeURIComponent(id)}/approve`, {
    method: "POST",
    body: JSON.stringify({ note: note?.trim() || null }),
  });
}

export function rejectPilotRegistration(id: string, reason: string) {
  return api<{ success: boolean; message: string }>(`/pilot-registrations/${encodeURIComponent(id)}/reject`, {
    method: "POST",
    body: JSON.stringify({ reason: reason.trim() }),
  });
}
