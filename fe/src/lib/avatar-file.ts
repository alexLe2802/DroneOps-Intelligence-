export const MAX_AVATAR_BYTES = 5 * 1024 * 1024;

const AVATAR_TYPES = new Set(["image/jpeg", "image/png", "image/webp"]);

export function avatarFileError(file: Pick<File, "type" | "size">): string | null {
  if (!AVATAR_TYPES.has(file.type) || file.size <= 0 || file.size > MAX_AVATAR_BYTES) {
    return "Choose a JPEG, PNG or WebP image up to 5 MB.";
  }
  return null;
}
