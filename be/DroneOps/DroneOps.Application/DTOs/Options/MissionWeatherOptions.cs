namespace DroneOps.Application.DTOs.Options;

public sealed class MissionWeatherOptions
{
    // =========================================================
    // FORECAST / ROUTE SAMPLING
    // =========================================================

    public int ForecastDays { get; set; } = 16;

    public int RequestBudgetSeconds { get; set; } = 20;

    public int CacheMinutes { get; set; } = 10;

    public decimal RouteSampleDistanceMeters { get; set; } = 1000;

    public int MaxRouteSamples { get; set; } = 60;

    // =========================================================
    // RISK LEVEL
    // =========================================================

    public int ReviewRiskScore { get; set; } = 25;

    public int HighRiskScore { get; set; } = 50;

    // =========================================================
    // WIND
    // =========================================================

    public decimal WindReviewMetersPerSecond { get; set; } = 5;

    public decimal WindHighMetersPerSecond { get; set; } = 10;

    public int WindWeight { get; set; } = 20;

    // =========================================================
    // WIND GUST
    // =========================================================

    public decimal GustReviewMetersPerSecond { get; set; } = 7;

    public decimal GustHighMetersPerSecond { get; set; } = 14;

    public int GustWeight { get; set; } = 15;

    // =========================================================
    // CROSSWIND
    // =========================================================

    public decimal CrosswindReviewMetersPerSecond { get; set; } = 4;

    public decimal CrosswindHighMetersPerSecond { get; set; } = 8;

    public int CrosswindWeight { get; set; } = 15;

    // =========================================================
    // RAIN
    // =========================================================

    public decimal RainReviewMillimetersPerHour { get; set; } = 0.2m;

    public decimal RainHighMillimetersPerHour { get; set; } = 1m;

    public int RainWeight { get; set; } = 15;

    // =========================================================
    // RAIN PROBABILITY
    // =========================================================

    public decimal RainProbabilityReviewPercent { get; set; } = 60;

    public decimal RainProbabilityHighPercent { get; set; } = 80;

    public int RainProbabilityWeight { get; set; } = 10;

    // =========================================================
    // VISIBILITY
    // =========================================================

    public decimal VisibilityReviewMeters { get; set; } = 5000;

    public decimal VisibilityHighMeters { get; set; } = 1000;

    public int VisibilityWeight { get; set; } = 10;

    // =========================================================
    // TEMPERATURE
    // =========================================================

    public decimal RecommendedMinTemperatureCelsius { get; set; } = 5;

    public decimal RecommendedMaxTemperatureCelsius { get; set; } = 40;

    public decimal OperatingMinTemperatureCelsius { get; set; } = -10;

    public decimal OperatingMaxTemperatureCelsius { get; set; } = 45;

    public int TemperatureWeight { get; set; } = 3;

    // =========================================================
    // HUMIDITY
    // =========================================================

    public decimal HumidityReviewPercent { get; set; } = 85;

    public decimal HumidityHighPercent { get; set; } = 95;

    public int HumidityWeight { get; set; } = 1;

    // =========================================================
    // CLOUD COVER
    // =========================================================

    public decimal CloudCoverReviewPercent { get; set; } = 70;

    public decimal CloudCoverHighPercent { get; set; } = 90;

    public int CloudCoverWeight { get; set; } = 1;

    // =========================================================
    // WEATHER CODE
    // =========================================================

    public int WeatherCodeWeight { get; set; } = 10;
}