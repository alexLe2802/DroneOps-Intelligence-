export type RegistryFile = { name: string; base64: string };

export async function readRegistryFile(file: File): Promise<RegistryFile> {
  if (!file.name.toLowerCase().endsWith(".pdf") || file.name.length > 180 || file.size > 2 * 1024 * 1024 ||
      await file.slice(0, 5).text() !== "%PDF-") throw new Error("Choose a PDF up to 2 MB with a filename of at most 180 characters.");
  const base64 = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result).split(",")[1]);
    reader.onerror = () => reject(new Error("The selected PDF could not be read."));
    reader.readAsDataURL(file);
  });
  return { name: file.name, base64 };
}
