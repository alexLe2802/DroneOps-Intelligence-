import type { NextConfig } from "next";
import path from "node:path";

const nextConfig = {
  output: "standalone",
  turbopack: {
    root: path.resolve(import.meta.dirname),
  },
} satisfies NextConfig;

export default nextConfig;
