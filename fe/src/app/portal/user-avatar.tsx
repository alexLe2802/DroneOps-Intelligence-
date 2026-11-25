import type { CSSProperties } from "react";

export default function UserAvatar({ src, name, size = 32, className }: { src?: string | null; name: string; size?: number; className?: string }) {
  const initials = name.trim().split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase() ?? "").join("") || "U";
  const boxStyle: CSSProperties = {
    display: "grid",
    placeItems: "center",
    width: size,
    height: size,
    flexShrink: 0,
    overflow: "hidden",
    borderRadius: "50%",
    background: "#003d7e",
    color: "#ffffff",
    fontSize: Math.max(12, Math.round(size * 0.36)),
    fontWeight: 700,
  };
  if (!src) return <span className={className} style={boxStyle}>{initials}</span>;
  return (
    <span className={className} style={boxStyle}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" width={size} height={size} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
    </span>
  );
}
