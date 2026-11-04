import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

test("pilot and manager UAV views expose images and the shared complete specification", async () => {
  const [list, detail, manager, information] = await Promise.all([
    readFile(new URL("../src/app/portal/uav/uav-client.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/app/portal/uav/[id]/uav-detail-client.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/app/portal/uav-management/uav-management-client.tsx", import.meta.url), "utf8"),
    readFile(new URL("../src/app/portal/uav/uav-information.tsx", import.meta.url), "utf8"),
  ]);
  assert.match(list, /uav\.imageUrl/);
  assert.match(detail, /<UavInformation aircraft=\{aircraft\}/);
  assert.match(manager, /selected\.imageUrl/);
  assert.match(manager, /<UavInformation aircraft=\{selected\}/);
  for (const field of ["manufacturer", "serialNumber", "emptyWeightKg", "maxTakeoffWeightKg", "maxPayloadKg", "batteryType", "batteryCapacityMah", "batteryVoltage", "batteryCellCount", "maxFlightTimeMinutes", "maxRangeKm", "maxSpeedKmh", "maxWindSpeedMps", "maxOperatingAltitudeMeters", "purchaseDate", "lastMaintenanceAt", "nextMaintenanceAt", "totalFlightHours", "totalFlights"]) {
    assert.match(information, new RegExp(`aircraft\\.${field}`), field);
  }
});
