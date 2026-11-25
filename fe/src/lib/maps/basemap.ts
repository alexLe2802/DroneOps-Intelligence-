import type * as Leaflet from "leaflet";

export const MAX_NATIVE_TILE_ZOOM = 19;
export const MAX_MAP_ZOOM = 24;

export function addBasemap(L: typeof Leaflet, map: Leaflet.Map, onTileError: () => void, onSwitch: () => void) {
  map.setMaxZoom(MAX_MAP_ZOOM);
  const service = "https://server.arcgisonline.com/ArcGIS/rest/services";
  const layer = (name: string, attribution: string) =>
    L.tileLayer(`${service}/${name}/MapServer/tile/{z}/{y}/{x}`, {
      attribution, maxNativeZoom: MAX_NATIVE_TILE_ZOOM, maxZoom: MAX_MAP_ZOOM, crossOrigin: true,
    }).on("tileerror", onTileError);

  const street = layer("World_Street_Map", "Tiles &copy; Esri");
  const satellite = layer("World_Imagery", "Imagery &copy; Esri and its imagery providers");
  const container = map.getContainer();
  const showSatellite = container.dataset.basemap === "satellite";
  (showSatellite ? satellite : street).addTo(map);
  container.classList.toggle("satellite-basemap", showSatellite);
  const control = new L.Control({ position: "topleft" });
  control.onAdd = () => {
    const button = L.DomUtil.create("button", "map-layer-toggle");
    button.type = "button";
    button.textContent = "Satellite";
    button.setAttribute("aria-label", "Satellite imagery");
    button.setAttribute("aria-pressed", String(showSatellite));
    L.DomEvent.disableClickPropagation(button);
    button.onclick = () => {
      const enabled = !map.hasLayer(satellite);
      map.removeLayer(enabled ? street : satellite);
      (enabled ? satellite : street).addTo(map);
      button.setAttribute("aria-pressed", String(enabled));
      container.dataset.basemap = enabled ? "satellite" : "street";
      container.classList.toggle("satellite-basemap", enabled);
      onSwitch();
    };
    return button;
  };
  control.addTo(map);
}
