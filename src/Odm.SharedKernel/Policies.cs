namespace Odm.SharedKernel;

/// <summary>Names of the authorization policies. Defined in the Identity module, used by all modules.</summary>
public static class Policies
{
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    /// <summary>Authenticated agent (own scheme, not for users).</summary>
    public const string Agent = "Agent";
}
