import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";
const source = await readFile(new URL("../src/lib/maps/planning-validation.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { polygonError, routeError, validCoordinate } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

const point = (longitude, latitude) => ({ latitude, longitude });
const triangle = [point(106, 10), point(107, 10), point(107, 11)];

test("coordinate boundaries accept zero and reject missing/non-finite values", () => {
  assert.equal(validCoordinate(point(0, 0)), true);
  assert.equal(validCoordinate(point(180, -90)), true);
  for (const invalid of [point(181, 0), point(0, -91), point(NaN, 10), point(106, Infinity)]) assert.equal(validCoordinate(invalid), false);
});
test("valid polygons support both winding orders and explicitly closed rings", () => {
  assert.equal(polygonError(triangle), null);
  assert.equal(polygonError([...triangle].reverse()), null);
  assert.equal(polygonError([...triangle, triangle[0]]), null);
});
test("incomplete, repeated, and collinear polygon vertices are rejected", () => {
  for (const points of [[], triangle.slice(0, 2), [triangle[0], triangle[1], triangle[0]], [...triangle, triangle[1]], [point(106, 10), point(107, 11), point(108, 12)]]) assert.ok(polygonError(points));
});
test("self-intersecting boundaries are rejected even with non-zero signed area", () => {
  assert.match(polygonError([point(0, 0), point(4, 3), point(0, 3), point(3, 0)]), /cross|overlap/);
});
test("out-of-range or cleared polygon coordinates cannot be saved", () => {
  assert.ok(polygonError([triangle[0], triangle[1], point(181, 10)]));
  assert.ok(polygonError([triangle[0], triangle[1], point(NaN, 10)]));
});
test("route validation rejects incomplete routes and non-finite altitude", () => {
  const route = triangle.slice(0, 2).map((p, i) => ({ ...p, altitude: 50, sequenceOrder: i + 1, actionType: "FlyThrough" }));
  assert.equal(routeError(route), null);
  assert.ok(routeError(route.slice(0, 1)));
  assert.ok(routeError([{ ...route[0], altitude: NaN }, route[1]]));
  assert.ok(routeError([{ ...route[0], altitude: -1 }, route[1]]));
  assert.ok(routeError([{ ...route[0], latitude: NaN }, route[1]]));
  assert.equal(routeError([{ ...route[0], altitude: 0 }, route[1]]), null);
  // The backend contract has no 500m maximum; don't invent an aviation limit.
  assert.equal(routeError([{ ...route[0], altitude: 501 }, route[1]]), null);
});
