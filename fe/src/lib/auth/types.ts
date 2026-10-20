export type AccountRole = "uav_operator" | "operation_manager";
export type Account = { avatarUrl?: string | null; id: string; email: string; displayName: string; role: AccountRole; isActive: boolean };
export type AuthSession = { account: Account; expiresAt: string };
export type SessionInfo = { id: string; createdAt: string; expiresAt: string; userAgent: string; isCurrent: boolean };
export const roleLabels: Record<AccountRole, string> = {
  uav_operator: "UAV Operator (Pilot)",
  operation_manager: "Operations Manager",
};
