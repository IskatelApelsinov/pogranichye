package sim

import "math"

// SurfaceProjectionVersion identifies the spherical surface projection used by global-world
// coordinates. Bump it only when persisted coordinate interpretation changes.
const SurfaceProjectionVersion = 1

// SurfacePoint is a geodetic position on the game planet. ElevationMeters is independent
// of the spherical surface coordinates; it is not folded into horizontal distance.
type SurfacePoint struct {
	LatitudeDeg  float64
	LongitudeDeg float64
	ElevationM   float64
}

// LocalMeters is a point in a settlement/region tangent plane. East and North are metres.
type LocalMeters struct {
	East  float64
	North float64
}

// PlanetProjection maps a spherical planet surface to an azimuthal-equidistant local plane.
// The projection preserves distance and direction from Origin and remains well-defined across
// the antimeridian and at polar origins (except the antipode, where direction is undefined).
type PlanetProjection struct {
	Origin       SurfacePoint
	RadiusMeters float64
}

const (
	degreesToRadians = math.Pi / 180
	radiansToDegrees = 180 / math.Pi
	projectionEpsilon = 1e-12
)

// NormalizeLongitude wraps longitude into [-180, 180). In particular +180 becomes -180.
func NormalizeLongitude(lon float64) float64 {
	if math.IsNaN(lon) || math.IsInf(lon, 0) {
		return math.NaN()
	}
	lon = math.Mod(lon+180, 360)
	if lon < 0 {
		lon += 360
	}
	return lon - 180
}

func validSurfacePoint(p SurfacePoint) bool {
	return !math.IsNaN(p.LatitudeDeg) && !math.IsInf(p.LatitudeDeg, 0) &&
		!math.IsNaN(p.LongitudeDeg) && !math.IsInf(p.LongitudeDeg, 0) &&
		!math.IsNaN(p.ElevationM) && !math.IsInf(p.ElevationM, 0) &&
		p.LatitudeDeg >= -90 && p.LatitudeDeg <= 90
}

// Forward projects a geodetic point to the local tangent plane. It returns false for invalid
// input or the exact antipode of the projection origin.
func (p PlanetProjection) Forward(point SurfacePoint) (LocalMeters, bool) {
	if p.RadiusMeters <= 0 || math.IsNaN(p.RadiusMeters) || math.IsInf(p.RadiusMeters, 0) ||
		!validSurfacePoint(p.Origin) || !validSurfacePoint(point) {
		return LocalMeters{}, false
	}
	lat0 := p.Origin.LatitudeDeg * degreesToRadians
	lat := point.LatitudeDeg * degreesToRadians
	dLon := NormalizeLongitude(point.LongitudeDeg-p.Origin.LongitudeDeg) * degreesToRadians
	sin0, cos0 := math.Sin(lat0), math.Cos(lat0)
	sinLat, cosLat := math.Sin(lat), math.Cos(lat)
	cosC := clampUnit(sin0*sinLat + cos0*cosLat*math.Cos(dLon))
	c := math.Acos(cosC)
	if c < projectionEpsilon {
		return LocalMeters{}, true
	}
	sinC := math.Sin(c)
	if math.Abs(sinC) < projectionEpsilon {
		return LocalMeters{}, false
	}
	k := c / sinC
	return LocalMeters{
		East:  p.RadiusMeters * k * cosLat * math.Sin(dLon),
		North: p.RadiusMeters * k * (cos0*sinLat - sin0*cosLat*math.Cos(dLon)),
	}, true
}

// Inverse maps local tangent-plane metres back to a geodetic point. The returned elevation is
// inherited from Origin; callers that model terrain height should assign it separately.
func (p PlanetProjection) Inverse(local LocalMeters) (SurfacePoint, bool) {
	if p.RadiusMeters <= 0 || math.IsNaN(p.RadiusMeters) || math.IsInf(p.RadiusMeters, 0) ||
		!validSurfacePoint(p.Origin) ||
		math.IsNaN(local.East) || math.IsInf(local.East, 0) ||
		math.IsNaN(local.North) || math.IsInf(local.North, 0) {
		return SurfacePoint{}, false
	}
	rho := math.Hypot(local.East, local.North)
	if rho < projectionEpsilon {
		return p.Origin, true
	}
	c := rho / p.RadiusMeters
	if c >= math.Pi-projectionEpsilon {
		return SurfacePoint{}, false
	}
	lat0 := p.Origin.LatitudeDeg * degreesToRadians
	sinC, cosC := math.Sin(c), math.Cos(c)
	sin0, cos0 := math.Sin(lat0), math.Cos(lat0)
	latArg := cosC*sin0 + local.North*sinC*cos0/rho
	lat := math.Asin(clampUnit(latArg))
	lon := p.Origin.LongitudeDeg + math.Atan2(
		local.East*sinC,
		rho*cos0*cosC-local.North*sin0*sinC,
	)*radiansToDegrees
	return SurfacePoint{
		LatitudeDeg:  lat * radiansToDegrees,
		LongitudeDeg: NormalizeLongitude(lon),
		ElevationM:   p.Origin.ElevationM,
	}, true
}

// GreatCircleMeters returns the shortest spherical surface distance between two points.
// Elevation is deliberately ignored.
func GreatCircleMeters(a, b SurfacePoint, radiusMeters float64) (float64, bool) {
	if radiusMeters <= 0 || math.IsNaN(radiusMeters) || math.IsInf(radiusMeters, 0) ||
		!validSurfacePoint(a) || !validSurfacePoint(b) {
		return 0, false
	}
	lat1, lat2 := a.LatitudeDeg*degreesToRadians, b.LatitudeDeg*degreesToRadians
	dLat := lat2 - lat1
	dLon := NormalizeLongitude(b.LongitudeDeg-a.LongitudeDeg) * degreesToRadians
	h := math.Sin(dLat/2)*math.Sin(dLat/2) +
		math.Cos(lat1)*math.Cos(lat2)*math.Sin(dLon/2)*math.Sin(dLon/2)
	return 2 * radiusMeters * math.Asin(math.Sqrt(clampUnit(h))), true
}

func clampUnit(v float64) float64 {
	if v < -1 {
		return -1
	}
	if v > 1 {
		return 1
	}
	return v
}

// ChunkOfMeters converts signed local coordinates to chunk indices. Mathematical floor is
// intentional: -1 metre belongs to chunk -1, not chunk 0. Chunk sizes must be positive.
func ChunkOfMeters(eastMeters, northMeters, chunkSizeMeters int64) (int64, int64, bool) {
	if chunkSizeMeters <= 0 {
		return 0, 0, false
	}
	return floorDiv(eastMeters, chunkSizeMeters), floorDiv(northMeters, chunkSizeMeters), true
}

func floorDiv(a, b int64) int64 {
	q, r := a/b, a%b
	if r != 0 && ((r < 0) != (b < 0)) {
		q--
	}
	return q
}
