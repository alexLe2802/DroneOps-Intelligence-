"use client";

import Image from "next/image";
import { useState } from "react";

export default function UserAvatar({ src, name, size = 40, className }: { src?: string | null; name: string; size?: number; className?: string }) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const initials = name.trim().split(/\s+/).filter(Boolean).slice(-2).map(part => part[0]).join("").toUpperCase() || "?";
  return <span className={className} style={{ display: "grid", placeItems: "center", width: size, height: size, flexShrink: 0, overflow: "hidden", borderRadius: "50%", background: "#15384b", color: "#8ee3ff", fontSize: Math.round(size * .32), fontWeight: 700 }}>
    {src && src !== failedSrc ? <Image unoptimized width={size} height={size} src={src} alt={`${name}'s avatar`} style={{ width: "100%", height: "100%", objectFit: "cover" }} onError={() => setFailedSrc(src)} /> : <span aria-label={`${name}'s initials`}>{initials}</span>}
  </span>;
}
