import { api } from "@/lib/auth/client";
export type DroneConnection = {
  id: string; uavId: string; missionId: string; createdAt: string; codeExpiresAt: string; expiresAt: string;
  hardwareIdentity: string | null;
  pairedAt: string | null; lastGatewayAt: string | null; lastSampleAt: string | null; revokedAt: string | null;
  gatewayName: string | null; systemId: number; componentId: number;
  status: "AwaitingGateway" | "PairingExpired" | "Expired" | "Revoked" | "Offline" | "GatewayConnected" | "ReceivingTelemetry";
};
export const getDroneConnections = () => api<DroneConnection[]>("/drone-connections");
export const createDroneConnection = (uavId: string, missionId: string) => api<{connection: DroneConnection; pairingCode: string}>("/drone-connections", {method:"POST",body:JSON.stringify({uavId,missionId})});
export const revokeDroneConnection = (id: string) => api<void>(`/drone-connections/${encodeURIComponent(id)}`,{method:"DELETE"});

export type DroneBinding = {uavId:string;hardwareIdentity:string;boundAt:string};
export const getDroneBindings = () => api<DroneBinding[]>("/drone-connections/aircraft");
