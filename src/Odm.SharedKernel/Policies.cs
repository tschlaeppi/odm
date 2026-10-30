namespace Odm.SharedKernel;

/// <summary>Namen der Autorisierungs-Richtlinien. Definiert im Identity-Modul, genutzt von allen Modulen.</summary>
public static class Policies
{
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    /// <summary>Authentisierter Agent (eigenes Schema, nicht für Benutzer).</summary>
    public const string Agent = "Agent";
}
