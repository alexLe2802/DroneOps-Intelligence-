"use client";

export type MapMode = "2d" | "3d";

/**
 * Compact toolbar for the mission map.
 * Controls: 2D/3D · Compass (reset north) · Fit Route · Fullscreen
 */
export default function MapToolbar({
  mode,
  onModeChange,
  onResetNorth,
  onFitRoute,
  onFullscreen,
  disabled = false,
  showModeToggle = true,
}: {
  mode: MapMode;
  onModeChange: (mode: MapMode) => void;
  onResetNorth: () => void;
  onFitRoute: () => void;
  onFullscreen: () => void;
  disabled?: boolean;
  showModeToggle?: boolean;
}) {
  return (
    <div className={`map-toolbar-3d${showModeToggle ? "" : " map-toolbar-actions"}`} role="toolbar" aria-label="Map controls">
      {showModeToggle && <div className="map-mode-toggle" role="group" aria-label="Map view mode">
        <button
          type="button"
          aria-pressed={mode === "2d"}
          onClick={() => onModeChange("2d")}
          disabled={disabled}
        >
          2D
        </button>
        <button
          type="button"
          aria-pressed={mode === "3d"}
          onClick={() => onModeChange("3d")}
          disabled={disabled}
        >
          3D
        </button>
      </div>}
      <button
        type="button"
        onClick={onResetNorth}
        disabled={disabled}
        aria-label="Reset north"
        title="Reset bearing to north"
        className="map-toolbar-btn"
      >
        ⊕
      </button>
      <button
        type="button"
        onClick={onFitRoute}
        disabled={disabled}
        aria-label="Zoom to fit route"
        title="Fit route in view"
        className="map-toolbar-btn"
      >
        ⊿
      </button>
      <button
        type="button"
        onClick={onFullscreen}
        disabled={disabled}
        aria-label="Toggle fullscreen"
        title="Fullscreen"
        className="map-toolbar-btn"
      >
        ⛶
      </button>
    </div>
  );
}
