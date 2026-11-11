package sim

import (
	"math"
	"testing"
)

func closeEnough(a, b, tolerance float64) bool { return math.Abs(a-b) <= tolerance }

func TestPlanetProjectionRoundTripsAcrossAntimeridian(t *testing.T) {
	projection := PlanetProjection{
		Origin: SurfacePoint{LatitudeDeg: 48.2, LongitudeDeg: 179.8, ElevationM: 125},
		RadiusMeters: 6_371_000,
	}
	target := SurfacePoint{LatitudeDeg: 48.35, LongitudeDeg: -179.7, ElevationM: 900}
	local, ok := projection.Forward(target)
	if !ok { t.Fatal("forward projection rejected antimeridian crossing") }
	got, ok := projection.Inverse(local)
	if !ok { t.Fatal("inverse projection rejected valid local point") }
	distance, ok := GreatCircleMeters(got, target, projection.RadiusMeters)
	if !ok || distance > 0.001 { t.Fatalf("round-trip surface error = %.6fm, want <= 0.001m", distance) }
	if got.ElevationM != projection.Origin.ElevationM {
		t.Fatalf("inverse elevation = %v, want origin elevation %v", got.ElevationM, projection.Origin.ElevationM)
	}
}

func TestPlanetProjectionRoundTripsColdAndHighLatitudeRegions(t *testing.T) {
	cases := []struct {
		name string
		origin, target SurfacePoint
	}{
		{"cold north", SurfacePoint{LatitudeDeg: 61, LongitudeDeg: 30}, SurfacePoint{LatitudeDeg: 63.5, LongitudeDeg: 34}},
		{"high north", SurfacePoint{LatitudeDeg: 84, LongitudeDeg: -40}, SurfacePoint{LatitudeDeg: 86, LongitudeDeg: 15}},
		{"near north pole", SurfacePoint{LatitudeDeg: 89.2, LongitudeDeg: 10}, SurfacePoint{LatitudeDeg: 89.5, LongitudeDeg: 170}},
		{"southern hemisphere", SurfacePoint{LatitudeDeg: -42, LongitudeDeg: 120}, SurfacePoint{LatitudeDeg: -39, LongitudeDeg: 118}},
		{"equator", SurfacePoint{LatitudeDeg: 0, LongitudeDeg: -5}, SurfacePoint{LatitudeDeg: 2, LongitudeDeg: 3}},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			projection := PlanetProjection{Origin: tc.origin, RadiusMeters: 6_000_000}
			local, ok := projection.Forward(tc.target)
			if !ok { t.Fatal("forward projection failed") }
			got, ok := projection.Inverse(local)
			if !ok { t.Fatal("inverse projection failed") }
			distance, ok := GreatCircleMeters(got, tc.target, projection.RadiusMeters)
			if !ok || distance > 0.001 {
				t.Fatalf("round-trip error = %.6fm, want <= 0.001m (got %+v)", distance, got)
			}
		})
	}
}

func TestPlanetProjectionRejectsInvalidAndAntipodalInputs(t *testing.T) {
	p := PlanetProjection{Origin: SurfacePoint{LatitudeDeg: 10, LongitudeDeg: 20}, RadiusMeters: 6_000_000}
	if _, ok := p.Forward(SurfacePoint{LatitudeDeg: 91, LongitudeDeg: 0}); ok {
		t.Fatal("invalid latitude accepted")
	}
	if _, ok := p.Forward(SurfacePoint{LatitudeDeg: -10, LongitudeDeg: -160}); ok {
		t.Fatal("antipodal point accepted although its bearing is undefined")
	}
	if _, ok := (PlanetProjection{Origin: p.Origin, RadiusMeters: 0}).Forward(p.Origin); ok {
		t.Fatal("zero-radius projection accepted")
	}
	if _, ok := p.Inverse(LocalMeters{East: math.NaN()}); ok {
		t.Fatal("NaN local coordinate accepted")
	}
}

func TestGreatCircleDistanceUsesShortestAntimeridianArc(t *testing.T) {
	a := SurfacePoint{LatitudeDeg: 0, LongitudeDeg: 179.9}
	b := SurfacePoint{LatitudeDeg: 0, LongitudeDeg: -179.9}
	got, ok := GreatCircleMeters(a, b, 6_371_000)
	if !ok { t.Fatal("valid points rejected") }
	want := 2 * math.Pi / 180 * 6_371_000
	if !closeEnough(got, want, 0.001) {
		t.Fatalf("distance = %.3f, want %.3f metres", got, want)
	}
}

func TestChunkOfMetersUsesFloorForNegativeCoordinates(t *testing.T) {
	cases := []struct{ east, north, size, wantX, wantY int64 }{
		{0, 0, 100, 0, 0},
		{99, 100, 100, 0, 1},
		{100, -1, 100, 1, -1},
		{-1, -100, 100, -1, -1},
		{-100, -101, 100, -1, -2},
		{-201, 201, 100, -3, 2},
	}
	for _, tc := range cases {
		x, y, ok := ChunkOfMeters(tc.east, tc.north, tc.size)
		if !ok || x != tc.wantX || y != tc.wantY {
			t.Errorf("ChunkOfMeters(%d,%d,%d) = (%d,%d,%v), want (%d,%d,true)",
				tc.east, tc.north, tc.size, x, y, ok, tc.wantX, tc.wantY)
		}
	}
	if _, _, ok := ChunkOfMeters(0, 0, 0); ok {
		t.Fatal("zero chunk size accepted")
	}
}
