import { bindings, defineConfig, defineWorker } from "cf/config";

export default defineConfig({
  accountId: "3b1490e057f7adcb19e8a45449766e69",
  worker: defineWorker({
    name: "droneops-web",
    entrypoint: "vinext/server/fetch-handler",
    compatibilityDate: "2026-11-04",
    compatibilityFlags: ["nodejs_compat"],
    domains: ["droneops.click", "www.droneops.click"],
    observability: { enabled: true, traces: { enabled: true } },
    assets: {
      htmlHandling: "none",
      notFoundHandling: "none",
      runWorkerFirst: ["/*", "!/_next/static/*", "!/*/_next/static/*"],
    },
    env: {
      ASSETS: bindings.assets(),
      API_BASE_URL: bindings.text("https://api.droneops.click"),
    },
  }),
});
