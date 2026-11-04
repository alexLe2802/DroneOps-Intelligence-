const DATABASE = "droneops-registration-evidence";
const STORE = "pilot-license-images";

export type StoredLicenseFile = {
  blob: Blob;
  fileName: string;
  contentType: string;
};

export type StoredLicenseEvidence = {
  email: string;
  files: StoredLicenseFile[];
  savedAt: string;
};

function openDatabase(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(DATABASE, 1);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(STORE)) request.result.createObjectStore(STORE, { keyPath: "email" });
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error("Unable to open the local license image store."));
  });
}

export async function saveLicenseFiles(email: string, files: File[]) {
  const database = await openDatabase();
  await new Promise<void>((resolve, reject) => {
    const transaction = database.transaction(STORE, "readwrite");
    transaction.objectStore(STORE).put({
      email: email.trim().toLowerCase(),
      files: files.map(file => ({ blob: file, fileName: file.name, contentType: file.type })),
      savedAt: new Date().toISOString(),
    } satisfies StoredLicenseEvidence);
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error("Unable to save the license image."));
  });
  database.close();
}

export async function getLicenseFiles(email: string): Promise<StoredLicenseEvidence | null> {
  const database = await openDatabase();
  const result = await new Promise<StoredLicenseEvidence & Partial<StoredLicenseFile> | undefined>((resolve, reject) => {
    const request = database.transaction(STORE, "readonly").objectStore(STORE).get(email.trim().toLowerCase());
    request.onsuccess = () => resolve(request.result as StoredLicenseEvidence & Partial<StoredLicenseFile> | undefined);
    request.onerror = () => reject(request.error ?? new Error("Unable to load the license image."));
  });
  database.close();
  if (!result) return null;
  if (Array.isArray(result.files)) return result;
  if (result.blob && result.fileName && result.contentType) return {
    email: result.email, savedAt: result.savedAt,
    files: [{ blob: result.blob, fileName: result.fileName, contentType: result.contentType }],
  };
  return null;
}
