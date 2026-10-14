namespace DroneOps.Domain;

public static class CanonicalRoles
{
    public static string Normalize(string role) => role.ToLowerInvariant() switch
    {
        "pilot" or "uav_operator" => "uav_operator",
        "admin" or "systemadmin" or "operationsmanager" or "operations_manager" or "operation_manager" => "operation_manager",
        _ => role
    };
}
