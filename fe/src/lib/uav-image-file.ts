export const MAX_UAV_IMAGE_BYTES = 5 * 1024 * 1024;

const UAV_IMAGE_TYPES = new Set(["image/jpeg", "image/png", "image/webp"]);

export function uavImageFileError(file: Pick<File, "type" | "size">): string | null {
  if (!UAV_IMAGE_TYPES.has(file.type)) return "Choose a JPEG, PNG or WebP UAV image.";
  if (file.size <= 0) return "The selected UAV image is empty.";
  if (file.size > MAX_UAV_IMAGE_BYTES) return "The UAV image must be 5 MB or smaller.";
  return null;
}
