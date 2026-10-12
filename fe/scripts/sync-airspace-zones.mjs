import { mkdir, writeFile } from "node:fs/promises";
import { VectorTile } from "@mapbox/vector-tile";
import polygonClipping from "polygon-clipping";
import { PbfReader } from "pbf";

const AIP_URL = "https://aim.vatm.vn/images/stories/vnaic.vn/SanPhamDichVu/AIPVietNam/AIP/2026-10-01-AIRAC/html/eAIP/VV-ENR-5.1-en-GB.html";
const MOD_TILE_URL = "https://cambay.mod.gov.vn/api/tiles/features/{z}/{x}/{y}.pbf";
const EFFECTIVE_DATE = "2026-10-01";
const OUTPUT_DIRECTORY = new URL("../public/data/", import.meta.url);

const AIRSPACE_TYPES = {
  VVP: { category: "prohibited", label: "Vùng cấm bay", color: "#ff0000" },
  VVR: { category: "restricted", label: "Vùng hạn chế bay", color: "#facc15" },
  VVD: { category: "danger", label: "Vùng nguy hiểm", color: "#facc15" },
};

function decodeHtml(value = "") {
  return value
    .replace(/<br\s*\/?>/gi, " ")
    .replace(/<[^>]+>/g, " ")
    .replace(/&nbsp;/gi, " ")
    .replace(/&amp;/gi, "&")
    .replace(/&quot;/gi, '"')
    .replace(/&#39;|&apos;/gi, "'")
    .replace(/\s+/g, " ")
    .trim();
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function parameterValue(row, parameter) {
  const pattern = new RegExp(
    `<span[^>]*class=["']SD["'][^>]*>((?:(?!<\\/span>)[\\s\\S])*)<\\/span>\\s*<span[^>]*class=["']sdParams["'][^>]*>${escapeRegex(parameter)};[^<]*<\\/span>`,
    "i",
  );
  return decodeHtml(row.match(pattern)?.[1]);
}

function coordinatePairs(row) {
  const pattern = /<span[^>]*class=["']SD["'][^>]*>((?:(?!<\/span>)[\s\S])*)<\/span>\s*<span[^>]*class=["']sdParams["'][^>]*>TAIRSPACE_VERTEX;GEO_LAT;[^<]*<\/span>\s*<span[^>]*class=["']SD["'][^>]*>((?:(?!<\/span>)[\s\S])*)<\/span>\s*<span[^>]*class=["']sdParams["'][^>]*>TAIRSPACE_VERTEX;GEO_LONG;[^<]*<\/span>/gi;
  return [...row.matchAll(pattern)].map(match => [dmsToDecimal(decodeHtml(match[2])), dmsToDecimal(decodeHtml(match[1]))]);
}

function dmsToDecimal(value) {
  const match = value.match(/^(\d{2,3})(\d{2})(\d{2}(?:\.\d+)?)([NSEW])$/i);
  if (!match) throw new Error(`Unsupported AIP coordinate: ${value}`);
  const degrees = Number(match[1]);
  const decimal = degrees + Number(match[2]) / 60 + Number(match[3]) / 3600;
  return /[SW]/i.test(match[4]) ? -decimal : decimal;
}

function closeRing(coordinates) {
  if (!coordinates.length) return coordinates;
  const first = coordinates[0];
  const last = coordinates.at(-1);
  return first[0] === last[0] && first[1] === last[1] ? coordinates : [...coordinates, first];
}

function circleRing(center, radiusKilometres, pointCount = 96) {
  const [longitude, latitude] = center;
  const angularDistance = radiusKilometres / 6371.0088;
  const latitudeRadians = latitude * Math.PI / 180;
  const longitudeRadians = longitude * Math.PI / 180;
  const coordinates = [];
  for (let index = 0; index <= pointCount; index += 1) {
    const bearing = 2 * Math.PI * index / pointCount;
    const destinationLatitude = Math.asin(
      Math.sin(latitudeRadians) * Math.cos(angularDistance)
      + Math.cos(latitudeRadians) * Math.sin(angularDistance) * Math.cos(bearing),
    );
    const destinationLongitude = longitudeRadians + Math.atan2(
      Math.sin(bearing) * Math.sin(angularDistance) * Math.cos(latitudeRadians),
      Math.cos(angularDistance) - Math.sin(latitudeRadians) * Math.sin(destinationLatitude),
    );
    coordinates.push([destinationLongitude * 180 / Math.PI, destinationLatitude * 180 / Math.PI]);
  }
  return coordinates;
}

function extractLimit(row, side) {
  const value = parameterValue(row, `TAIRSPACE_VOLUME;VAL_DIST_VER_${side}`);
  const unit = parameterValue(row, `TAIRSPACE_VOLUME;UOM_DIST_VER_${side}`);
  const reference = parameterValue(row, `TAIRSPACE_VOLUME;CODE_DIST_VER_${side}`);
  return [value, unit, reference].filter(Boolean).join(" ");
}

function parseAip(html) {
  const rows = [...html.matchAll(/<tr\b[^>]*>([\s\S]*?)<\/tr>/gi)].map(match => match[1]);
  const unresolved = [];
  const features = [];
  for (const row of rows) {
    if (!row.includes("TAIRSPACE;CODE_ID")) continue;
    const code = parameterValue(row, "TAIRSPACE;CODE_ID");
    const type = AIRSPACE_TYPES[code.slice(0, 3)];
    if (!type) continue;
    let ring = coordinatePairs(row);
    let geometryMethod = "published_vertices";
    if (ring.length < 3) {
      const latitude = parameterValue(row, "TAIRSPACE_VERTEX;GEO_LAT_ARC");
      const longitude = parameterValue(row, "TAIRSPACE_VERTEX;GEO_LONG_ARC");
      const radius = Number(parameterValue(row, "TAIRSPACE_VERTEX;VAL_RADIUS_ARC"));
      const unit = parameterValue(row, "TAIRSPACE_VERTEX;UOM_RADIUS_ARC").toUpperCase();
      if (latitude && longitude && Number.isFinite(radius)) {
        const radiusKilometres = unit === "NM" ? radius * 1.852 : unit === "M" ? radius / 1000 : radius;
        ring = circleRing([dmsToDecimal(longitude), dmsToDecimal(latitude)], radiusKilometres);
        geometryMethod = "generated_from_published_circle";
      }
    }
    const properties = {
      code,
      name: parameterValue(row, "TAIRSPACE;TXT_NAME") || code,
      category: type.category,
      categoryLabel: type.label,
      color: type.color,
      lowerLimit: extractLimit(row, "LOWER") || null,
      upperLimit: extractLimit(row, "UPPER") || null,
      source: "AIP_VIETNAM_ENR_5.1",
      sourceUrl: AIP_URL,
      effectiveDate: EFFECTIVE_DATE,
      geometryMethod,
      reconciliation: "pending",
    };
    if (ring.length >= 4) {
      features.push({ type: "Feature", properties, geometry: { type: "Polygon", coordinates: [closeRing(ring)] } });
    } else {
      unresolved.push({ code, name: properties.name, reason: "AIP description has no directly machine-readable polygon or circle" });
    }
  }
  return { features, unresolved };
}

function lonToTile(longitude, zoom) {
  return Math.floor((longitude + 180) / 360 * 2 ** zoom);
}

function latToTile(latitude, zoom) {
  return Math.floor((1 - Math.asinh(Math.tan(latitude * Math.PI / 180)) / Math.PI) / 2 * 2 ** zoom);
}

function geometryToPolygons(geometry) {
  if (geometry.type === "Polygon") return [geometry.coordinates];
  if (geometry.type === "MultiPolygon") return geometry.coordinates;
  return [];
}

async function loadModPolygons() {
  // The public tile payload currently uses layer_id 1 for CB (cam bay) and
  // layer_id 2 for HCB (han che bay). The identity codes returned by the
  // public feature-details endpoint were used to verify this mapping.
  const zoom = 6;
  const polygons = { prohibited: [], restricted: [] };
  const tileUrls = [];
  for (let x = lonToTile(101, zoom); x <= lonToTile(111.5, zoom); x += 1) {
    for (let y = latToTile(24.5, zoom); y <= latToTile(6.5, zoom); y += 1) {
      tileUrls.push(MOD_TILE_URL.replace("{z}", zoom).replace("{x}", x).replace("{y}", y));
    }
  }
  await Promise.all(tileUrls.map(async url => {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`MOD tile request failed (${response.status}): ${url}`);
    const tile = new VectorTile(new PbfReader(new Uint8Array(await response.arrayBuffer())));
    const layer = tile.layers.features;
    if (!layer) return;
    const match = url.match(/features\/(\d+)\/(\d+)\/(\d+)\.pbf$/);
    const [, z, x, y] = match.map(Number);
    for (let index = 0; index < layer.length; index += 1) {
      const feature = layer.feature(index);
      const category = feature.properties.layer_id === 1 ? "prohibited" : feature.properties.layer_id === 2 ? "restricted" : null;
      if (!category) continue;
      const geoJson = feature.toGeoJSON(x, y, z);
      polygons[category].push(...geometryToPolygons(geoJson.geometry));
    }
  }));
  return { polygons, tileCount: tileUrls.length };
}

function unionInBatches(polygons, batchSize = 150) {
  let merged = [];
  for (let index = 0; index < polygons.length; index += batchSize) {
    const batch = polygons.slice(index, index + batchSize);
    const batchUnion = polygonClipping.union(...batch);
    merged = merged.length ? polygonClipping.union(merged, batchUnion) : batchUnion;
  }
  return merged;
}

function hasIntersection(a, b) {
  if (!a.length || !b.length) return false;
  return polygonClipping.intersection(a, b).length > 0;
}

function difference(a, subtractors) {
  if (!a.length) return [];
  return subtractors.length ? polygonClipping.difference(a, ...subtractors) : a;
}

function polygonBoundsArea(polygons) {
  const positions = polygons.flat(2);
  if (!positions.length) return 0;
  const longitudes = positions.map(position => position[0]);
  const latitudes = positions.map(position => position[1]);
  return (Math.max(...longitudes) - Math.min(...longitudes)) * (Math.max(...latitudes) - Math.min(...latitudes));
}

function removeFullyContainedFeatures(features) {
  const retained = [];
  const removed = [];
  for (const category of ["prohibited", "restricted", "danger"]) {
    const candidates = features
      .filter(feature => feature.properties.category === category)
      .sort((left, right) => polygonBoundsArea(geometryToPolygons(right.geometry)) - polygonBoundsArea(geometryToPolygons(left.geometry)));
    for (const candidate of candidates) {
      const candidatePolygons = geometryToPolygons(candidate.geometry);
      const coveringFeature = retained.find(feature =>
        feature.properties.category === category
        && polygonClipping.difference(candidatePolygons, geometryToPolygons(feature.geometry)).length === 0,
      );
      if (coveringFeature) {
        removed.push({ code: candidate.properties.code, coveredBy: coveringFeature.properties.code, category });
      } else {
        retained.push(candidate);
      }
    }
  }
  return { retained, removed };
}

async function main() {
  console.log(`Downloading AIP ENR 5.1 (${EFFECTIVE_DATE})...`);
  const aipResponse = await fetch(AIP_URL);
  if (!aipResponse.ok) throw new Error(`AIP request failed (${aipResponse.status})`);
  const aip = parseAip(await aipResponse.text());
  console.log(`Parsed ${aip.features.length} AIP geometries; ${aip.unresolved.length} textual definitions need review.`);
  const deduplicatedAip = removeFullyContainedFeatures(aip.features);
  console.log(`Removed ${deduplicatedAip.removed.length} AIP geometries fully covered by a wider area of the same type.`);

  console.log("Downloading public MOD vector tiles...");
  const mod = await loadModPolygons();
  console.log(`Dissolving ${mod.polygons.prohibited.length} prohibited and ${mod.polygons.restricted.length} restricted polygon fragments...`);
  const modUnion = {
    prohibited: unionInBatches(mod.polygons.prohibited),
    restricted: unionInBatches(mod.polygons.restricted),
  };

  const aipByCategory = { prohibited: [], restricted: [], danger: [] };
  for (const feature of deduplicatedAip.retained) {
    const category = feature.properties.category;
    const polygons = geometryToPolygons(feature.geometry);
    aipByCategory[category].push(...polygons);
    feature.properties.reconciliation = category === "danger"
      ? "aip_only"
      : hasIntersection(polygons, modUnion[category]) ? "matched_spatially" : "aip_only";
  }

  const supplemental = [];
  for (const category of ["prohibited", "restricted"]) {
    const remainder = difference(modUnion[category], aipByCategory[category]);
    if (!remainder.length) continue;
    supplemental.push({
      type: "Feature",
      properties: {
        code: category === "prohibited" ? "MOD-CB-SUPPLEMENT" : "MOD-HCB-SUPPLEMENT",
        name: category === "prohibited" ? "Vùng cấm bay bổ sung từ Cổng Bộ Quốc phòng" : "Vùng hạn chế bay bổ sung từ Cổng Bộ Quốc phòng",
        category,
        categoryLabel: category === "prohibited" ? "Vùng cấm bay" : "Vùng hạn chế bay",
        color: category === "prohibited" ? "#ff0000" : "#facc15",
        lowerLimit: null,
        upperLimit: null,
        source: "CAMBAY_MOD_GOV_VN_PUBLIC_VECTOR_TILES",
        sourceUrl: "https://cambay.mod.gov.vn/",
        effectiveDate: new Date().toISOString().slice(0, 10),
        geometryMethod: "dissolved_public_vector_tiles",
        reconciliation: "mod_only_supplement",
      },
      geometry: { type: "MultiPolygon", coordinates: remainder },
    });
  }

  const generatedAt = new Date().toISOString();
  const collection = {
    type: "FeatureCollection",
    name: "Vietnam AIP ENR 5.1 airspace reconciled with cambay.mod.gov.vn",
    metadata: {
      generatedAt,
      aipEffectiveDate: EFFECTIVE_DATE,
      aipSource: AIP_URL,
      modSource: "https://cambay.mod.gov.vn/api/tiles/features/{z}/{x}/{y}.pbf",
      warning: "Operational planning aid only. Always verify against current NOTAM and official publications.",
    },
    features: [...deduplicatedAip.retained, ...supplemental],
  };
  const counts = collection.features.reduce((result, feature) => {
    const key = `${feature.properties.category}:${feature.properties.reconciliation}`;
    result[key] = (result[key] ?? 0) + 1;
    return result;
  }, {});
  const metadata = {
    generatedAt,
    aipEffectiveDate: EFFECTIVE_DATE,
    aipFeatureCount: deduplicatedAip.retained.length,
    removedContainedAipFeatures: deduplicatedAip.removed,
    modTileCount: mod.tileCount,
    unresolvedAipDefinitions: aip.unresolved,
    counts,
    sources: [AIP_URL, "https://cambay.mod.gov.vn/"],
    methodology: "AIP polygons/circles are the baseline. Public MOD vector-tile polygons are dissolved by CB/HCB type; spatial remainder is added as a supplementary layer. aip_only is retained for review and is never auto-deleted.",
  };

  await mkdir(OUTPUT_DIRECTORY, { recursive: true });
  await Promise.all([
    writeFile(new URL("airspace-zones.geojson", OUTPUT_DIRECTORY), `${JSON.stringify(collection)}\n`),
    writeFile(new URL("airspace-zones-meta.json", OUTPUT_DIRECTORY), `${JSON.stringify(metadata, null, 2)}\n`),
  ]);
  console.log("Wrote public/data/airspace-zones.geojson and airspace-zones-meta.json", counts);
}

await main();
