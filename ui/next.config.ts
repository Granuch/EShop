import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // A self-contained server (.next/standalone) for the container image: ui/Dockerfile copies it without node_modules.
  output: "standalone",
  images: {
    // Browsers load product photos straight from their host (a CDN). Through the optimizer, this server fetched
    // each one first, and inside Docker Desktop that fetch sometimes timed out ("upstream image response timed
    // out"), so a first visit showed alt text instead of photos (2026-10-10). The photos are small WebP already.
    unoptimized: true,
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'picsum.photos',
      },
      // The demo catalog's product photos (infrastructure/demo-data/seed_catalog.py).
      {
        protocol: 'https',
        hostname: 'cdn.dummyjson.com',
        pathname: '/product-images/**',
      },
    ],
  }
};

export default nextConfig;
