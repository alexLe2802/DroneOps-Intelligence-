const fields = {
  pilot: {
    "Full name": "fullName",
    "Email": "email",
    "Phone number": "phoneNumber",
    "Date of birth (YYYY-MM-DD)": "dateOfBirth",
    "Pilot license number": "pilotLicenseNo",
    "Experience years": "experienceYears",
    "License issued date (YYYY-MM-DD)": "licenseIssuedDate",
    "License expiration date (YYYY-MM-DD)": "licenseExpiredDate",
    "Flight experience": "description",
  },
  uav: {
    "UAV code": "code",
    "UAV name": "name",
    "Manufacturer": "manufacturer",
    "Model": "model",
    "Serial number": "serialNumber",
    "Empty weight (kg)": "emptyWeightKg",
    "Maximum takeoff weight (kg)": "maxTakeoffWeightKg",
    "Maximum payload (kg)": "maxPayloadKg",
    "Battery type": "batteryType",
    "Pack capacity (mAh)": "batteryCapacityMah",
    "Maximum pack voltage (V)": "batteryMaxVoltage",
    "Cell count": "batteryCellCount",
    "Maximum flight time (min)": "maxFlightTimeMinutes",
    "Maximum range (km)": "maxRangeKm",
    "Maximum wind resistance (m/s)": "maxWindSpeedMps",
  },
};

export type TemplateKind = keyof typeof fields;
const title = (kind: TemplateKind) => `DroneOps ${kind.toUpperCase()} registration v1`;

export function registrationTemplate(kind: TemplateKind) {
  return [
    title(kind),
    "# Enter each value after the colon on one line. Blank values keep the current form entries.",
    kind === "pilot"
      ? "# Enter your password and attach your license on the registration page. Dates can use YYYY-MM-DD or DD/MM/YYYY."
      : "# Choose the aircraft photo on the registration page. Use a dot or comma for decimals and omit thousands separators.",
    ...Object.keys(fields[kind]).map(label => `${label}: `),
    "",
  ].join("\r\n");
}

export function parseRegistrationTemplate(kind: TemplateKind, text: string) {
  const [header, ...lines] = text.trimStart().split(/\r?\n/);
  if (header.trim() !== title(kind)) throw new Error("Choose the template downloaded from this form.");
  const names = new Map(Object.entries(fields[kind]));
  const entries = new Map<string, string>();
  const seen = new Set<string>();
  for (const [index, line] of lines.entries()) {
    if (!line.trim() || line.trimStart().startsWith("#")) continue;
    const separator = line.indexOf(":");
    const name = names.get(line.slice(0, separator).trim());
    if (separator < 0 || !name || seen.has(name)) throw new Error(`Check the field label on line ${index + 2}.`);
    seen.add(name);
    const value = line.slice(separator + 1).trim();
    if (value) entries.set(name, value);
  }
  if (!entries.size) throw new Error("Fill in at least one field before importing the template.");
  return entries;
}

export function normalizeTemplateValue(type: string, value: string) {
  if (type === "date") {
    const date = /^(\d{1,2})([/.\-])(\d{1,2})\2(\d{4})$/.exec(value);
    if (date) return `${date[4]}-${date[3].padStart(2, "0")}-${date[1].padStart(2, "0")}`;
  }
  if (type === "number" && /^-?\d+,\d+$/.test(value)) return value.replace(",", ".");
  return value;
}
