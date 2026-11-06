"use client";

import Link from "next/link";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { createUav, updateAssignedUav, uploadUavImage } from "@/lib/portal/client";
import type { AssignedUav, CreateUavRequest } from "@/lib/portal/types";
import { optionalNumber, uavSpecificationError } from "@/lib/uav-specification";
import { uavImageFileError } from "@/lib/uav-image-file";

type UavFormValue = Record<keyof CreateUavRequest, string>;

function dateInputValue(value: string | null | undefined) {
  return value ? value.slice(0, 10) : "";
}

function initialValue(aircraft?: AssignedUav): UavFormValue {
  const value = (item: string | number | null | undefined) => item == null ? "" : String(item);
  return {
    code: aircraft?.code ?? "",
    name: aircraft?.name ?? "",
    manufacturer: aircraft?.manufacturer ?? "",
    model: aircraft?.model ?? "",
    serialNumber: aircraft?.serialNumber ?? "",
    emptyWeightKg: value(aircraft?.emptyWeightKg),
    maxTakeoffWeightKg: value(aircraft?.maxTakeoffWeightKg),
    maxPayloadKg: value(aircraft?.maxPayloadKg),
    batteryType: aircraft?.batteryType ?? "",
    batteryCapacityMah: value(aircraft?.batteryCapacityMah),
    batteryVoltage: value(aircraft?.batteryVoltage),
    batteryMaxVoltage: value(aircraft?.batteryMaxVoltage),
    batteryCellCount: value(aircraft?.batteryCellCount),
    maxFlightTimeMinutes: value(aircraft?.maxFlightTimeMinutes),
    maxRangeKm: value(aircraft?.maxRangeKm),
    maxSpeedKmh: value(aircraft?.maxSpeedKmh),
    maxWindSpeedMps: value(aircraft?.maxWindSpeedMps),
    maxOperatingAltitudeMeters: value(aircraft?.maxOperatingAltitudeMeters),
    purchaseDate: dateInputValue(aircraft?.purchaseDate),
  };
}

export default function UavForm({ aircraft }: { aircraft?: AssignedUav }) {
  const pending = useRef(false);
  const imageInput = useRef<HTMLInputElement>(null);
  const router = useRouter();
  const [form, setForm] = useState(() => initialValue(aircraft));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [imagePreview, setImagePreview] = useState("");
  const [imageError, setImageError] = useState("");
  const set = (field: keyof UavFormValue, value: string) => setForm(current => ({ ...current, [field]: value }));
  useEffect(() => () => { if (imagePreview) URL.revokeObjectURL(imagePreview); }, [imagePreview]);

  function clearPendingImage() {
    setImageFile(null);
    setImagePreview("");
    if (imageInput.current) imageInput.current.value = "";
  }

  function chooseImage(file: File | undefined) {
    setImageError("");
    if (!file) return;
    const validationError = uavImageFileError(file);
    if (validationError) {
      clearPendingImage();
      setImageError(validationError);
      return;
    }
    setImageFile(file);
    setImagePreview(URL.createObjectURL(file));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (pending.current) return;

    const request: CreateUavRequest = {
      code: form.code.trim().toUpperCase(),
      name: form.name.trim(),
      manufacturer: form.manufacturer.trim() || null,
      model: form.model.trim() || null,
      serialNumber: form.serialNumber.trim() || null,
      emptyWeightKg: optionalNumber(form.emptyWeightKg),
      maxTakeoffWeightKg: optionalNumber(form.maxTakeoffWeightKg),
      maxPayloadKg: optionalNumber(form.maxPayloadKg),
      batteryType: form.batteryType.trim() || null,
      batteryCapacityMah: optionalNumber(form.batteryCapacityMah),
      batteryVoltage: optionalNumber(form.batteryVoltage),
      batteryMaxVoltage: optionalNumber(form.batteryMaxVoltage ?? ""),
      batteryCellCount: optionalNumber(form.batteryCellCount),
      maxFlightTimeMinutes: optionalNumber(form.maxFlightTimeMinutes),
      maxRangeKm: optionalNumber(form.maxRangeKm),
      maxSpeedKmh: optionalNumber(form.maxSpeedKmh),
      maxWindSpeedMps: optionalNumber(form.maxWindSpeedMps),
      maxOperatingAltitudeMeters: optionalNumber(form.maxOperatingAltitudeMeters),
      purchaseDate: form.purchaseDate ? `${form.purchaseDate}T00:00:00.000Z` : null,
    };

    if (!request.code || !request.name) {
      setError("UAV code and name are required.");
      return;
    }
    const specificationError = uavSpecificationError(request);
    if (specificationError) {
      setError(specificationError);
      return;
    }
    if (imageFile) {
      const validationError = uavImageFileError(imageFile);
      if (validationError) {
        clearPendingImage();
        setImageError(validationError);
        return;
      }
    }

    pending.current = true;
    setSaving(true);
    setError("");
    try {
      let id: string;
      let submitted: "create" | "update";
      if (aircraft) {
        await updateAssignedUav(aircraft.id, request);
        id = aircraft.id;
        submitted = "update";
      } else {
        const result = await createUav(request);
        id = result.id;
        submitted = "create";
      }
      let imageUploadFailed = false;
      if (imageFile) {
        try { await uploadUavImage(id, imageFile); }
        catch { imageUploadFailed = true; }
      }
      router.push(`/portal/uav/${id}?submitted=${submitted}${imageUploadFailed ? "&imageUpload=failed" : imageFile ? "&imageUpload=success" : ""}`);
      router.refresh();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to submit the UAV request.");
    } finally {
      pending.current = false;
      setSaving(false);
    }
  }

  return <>
    <header className="module-heading">
      <div>
        <p className="module-kicker">PILOT / UAV REQUEST</p>
        <h1>{aircraft ? "Edit UAV" : "Add UAV"}</h1>
        <p>{aircraft ? "Update aircraft identity, weight and battery information." : "Register an aircraft and send its technical specifications for approval."}</p>
      </div>
    </header>

    <form className="uav-form" onSubmit={submit}>
      <fieldset className="uav-form-fields" disabled={saving}>
        <legend className="sr-only">Aircraft details</legend>

        <section className="uav-form-section">
          <header><span>01</span><div><h2>Aircraft identity</h2><p>Registration and manufacturer information.</p></div></header>
          <div className="uav-form-grid">
            <label>UAV CODE<input value={form.code} onChange={event => set("code", event.target.value)} placeholder="UAV-08" minLength={2} maxLength={50} required /></label>
            <label>UAV NAME<input value={form.name} onChange={event => set("name", event.target.value)} placeholder="South Survey Two" minLength={2} maxLength={100} required /></label>
            <label>MANUFACTURER<input value={form.manufacturer} onChange={event => set("manufacturer", event.target.value)} placeholder="DJI" maxLength={100} /></label>
            <label>MODEL<input value={form.model} onChange={event => set("model", event.target.value)} placeholder="Matrice 350 RTK" maxLength={100} /></label>
            <label className="uav-form-wide">SERIAL NUMBER<input value={form.serialNumber} onChange={event => set("serialNumber", event.target.value)} placeholder="SN-2026-0001" maxLength={100} /></label>
          </div>
        </section>

        <section className="uav-form-section">
          <header><span>02</span><div><h2>Physical specifications</h2><p>Weight values are measured in kilograms.</p></div></header>
          <div className="uav-form-grid uav-form-grid-three">
            <label>EMPTY WEIGHT (KG)<input type="number" value={form.emptyWeightKg} onChange={event => set("emptyWeightKg", event.target.value)} placeholder="6.3" min="0.001" step="0.001" /></label>
            <label>MAX TAKEOFF WEIGHT (KG)<input type="number" value={form.maxTakeoffWeightKg} onChange={event => set("maxTakeoffWeightKg", event.target.value)} placeholder="9.2" min="0.001" step="0.001" /></label>
            <label>MAX PAYLOAD (KG)<input type="number" value={form.maxPayloadKg} onChange={event => set("maxPayloadKg", event.target.value)} placeholder="2.7" min="0" step="0.001" /></label>
          </div>
          <p className="field-help">Empty weight + maximum payload cannot exceed the maximum takeoff weight.</p>
        </section>

        <section className="uav-form-section">
          <header><span>03</span><div><h2>Battery system</h2><p>Electrical configuration and rated capacity.</p></div></header>
          <div className="uav-form-grid">
            <label>BATTERY TYPE<input value={form.batteryType} onChange={event => set("batteryType", event.target.value)} placeholder="LiPo 6S" maxLength={50} /></label>
            <label>PACK CAPACITY (mAh) · OPTIONAL<input type="number" value={form.batteryCapacityMah} onChange={event => set("batteryCapacityMah", event.target.value)} placeholder="Read from FC when connected" min="1" step="1" /><small>Leave blank if unknown. FC capacity is preferred when available; enter a fallback only if you know the installed pack capacity.</small></label>
            <label>MAX PACK VOLTAGE (V)<input type="number" value={form.batteryMaxVoltage} onChange={event => set("batteryMaxVoltage", event.target.value)} placeholder="25.2 for standard 6S LiPo" min="0.01" step="0.01" /><small>Full-charge voltage for the whole installed pack. Use the battery label; this is different from nominal voltage and live telemetry.</small></label>
            <label>CELL COUNT<input type="number" value={form.batteryCellCount} onChange={event => set("batteryCellCount", event.target.value)} placeholder="6" min="1" max="100" step="1" /></label>
          </div>
        </section>

        <section className="uav-form-section">
          <header><span>04</span><div><h2>Endurance information</h2><p>Optional manufacturer-rated endurance. Automatic speed, altitude and wind limits are set in the mission.</p></div></header>
          <div className="uav-form-grid uav-form-grid-three">
            <label>MAX FLIGHT TIME (MIN)<input type="number" value={form.maxFlightTimeMinutes} onChange={event => set("maxFlightTimeMinutes", event.target.value)} placeholder="45" min="1" step="1" /></label>
            <label>MAX RANGE (KM)<input type="number" value={form.maxRangeKm} onChange={event => set("maxRangeKm", event.target.value)} placeholder="15" min="0.01" step="0.01" /></label>
          </div>
        </section>

        <section className="uav-form-section">
          <header><span>05</span><div><h2>Acquisition</h2><p>Optional purchase information for the fleet record.</p></div></header>
          <div className="uav-form-grid">
            <label>PURCHASE DATE<input type="date" value={form.purchaseDate} onChange={event => set("purchaseDate", event.target.value)} max="9999-12-31" /></label>
          </div>
        </section>

        <section className="uav-form-section">
          <header><span>06</span><div><h2>Aircraft image</h2><p>Add a clear registry photo of the UAV.</p></div></header>
          <div className="uav-image-editor">
            <div className="uav-image-preview">
              {imagePreview || aircraft?.imageUrl
                ? <Image src={imagePreview || aircraft!.imageUrl!} alt={`${form.name || form.code || "UAV"} preview`} width={720} height={420} unoptimized />
                : <div><span>UAV</span><small>No image selected</small></div>}
            </div>
            <div className="uav-image-controls">
              <div className="uav-image-field">
                <span>UAV IMAGE</span>
                <div className="uav-image-select">
                  <input ref={imageInput} className="uav-image-file-input" type="file" accept="image/jpeg,image/png,image/webp" aria-label="Choose UAV image" aria-describedby="uav-image-help uav-image-error" aria-invalid={Boolean(imageError)} onChange={event => chooseImage(event.target.files?.[0])} />
                  <button type="button" onClick={() => imageInput.current?.click()}>CHOOSE UAV IMAGE</button>
                  <small>{imageFile?.name ?? (aircraft?.imageUrl ? "Current image will be kept" : "No image selected")}</small>
                </div>
              </div>
              <p id="uav-image-help">JPEG, PNG or WebP · maximum 5 MB. A new image replaces the current image after save.</p>
              {imageFile && <button type="button" className="module-secondary" onClick={clearPendingImage}>REMOVE SELECTED IMAGE</button>}
              {imageError && <p className="uav-image-error" id="uav-image-error" role="alert">{imageError}</p>}
            </div>
          </div>
        </section>
      </fieldset>

      <p className="uav-form-notice">{aircraft ? "Editing technical information may return the UAV to manager review, depending on the backend workflow." : "The UAV is created with Pending status and becomes operational after manager approval."}</p>
      {error && <p className="form-error" role="alert" aria-live="assertive">{error}</p>}
      <div className="form-actions">
        <Link className="module-secondary module-link" href={aircraft ? `/portal/uav/${aircraft.id}` : "/portal/uav"}>CANCEL</Link>
        <button className="module-primary" disabled={saving}>{saving ? "SAVING..." : aircraft ? "SAVE CHANGES" : "SUBMIT FOR APPROVAL"}</button>
      </div>
    </form>
  </>;
}
