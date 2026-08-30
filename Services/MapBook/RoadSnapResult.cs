namespace Application.Services.MapBook;

public sealed record RoadSnapResult(
  double Lat,
  double Lng,
  double DistanceMeters,
  string Source);
