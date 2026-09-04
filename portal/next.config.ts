import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Emits a self-contained .next/standalone build (server.js + only the
  // node_modules actually used) so the production Docker image doesn't need
  // npm/node_modules at runtime — see portal/Dockerfile.
  output: "standalone",
};

export default nextConfig;
