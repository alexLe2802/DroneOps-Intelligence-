const DATABASE = "droneops-registration-evidence";
const STORE = "pilot-license-images";

export type StoredLicenseImage = {
  email: string;
  blob: Blob;
  fileName: string;
  contentType: string;
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

export async function saveLicenseImage(email: string, file: File) {
  const database = await openDatabase();
  await new Promise<void>((resolve, reject) => {
    const transaction = database.transaction(STORE, "readwrite");
    transaction.objectStore(STORE).put({
      email: email.trim().toLowerCase(), blob: file, fileName: file.name,
      contentType: file.type, savedAt: new Date().toISOString(),
    } satisfies StoredLicenseImage);
    transaction.oncomplete = () => resolve();
    transaction.onerror = () => reject(transaction.error ?? new Error("Unable to save the license image."));
  });
  database.close();
}

export async function getLicenseImage(email: string): Promise<StoredLicenseImage | null> {
  const database = await openDatabase();
  const result = await new Promise<StoredLicenseImage | undefined>((resolve, reject) => {
    const request = database.transaction(STORE, "readonly").objectStore(STORE).get(email.trim().toLowerCase());
    request.onsuccess = () => resolve(request.result as StoredLicenseImage | undefined);
    request.onerror = () => reject(request.error ?? new Error("Unable to load the license image."));
  });
  database.close();
  return result ?? null;
}
