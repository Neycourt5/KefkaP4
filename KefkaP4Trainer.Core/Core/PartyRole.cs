namespace KefkaP4Trainer.Core;

public enum PartyRole
{
    T1,
    T2,
    H1,
    H2,
    M1,
    M2,
    R1,
    R2,
}

public static class PartyRoles
{
    public static readonly PartyRole[] All =
    [
        PartyRole.T1, PartyRole.T2, PartyRole.H1, PartyRole.H2,
        PartyRole.M1, PartyRole.M2, PartyRole.R1, PartyRole.R2,
    ];

    public static readonly PartyRole[] Supports =
        [PartyRole.T1, PartyRole.T2, PartyRole.H1, PartyRole.H2];

    public static readonly PartyRole[] Dps =
        [PartyRole.M1, PartyRole.M2, PartyRole.R1, PartyRole.R2];

    public static bool IsDps(this PartyRole role) => role >= PartyRole.M1;

    public static string Key(this PartyRole role) => role.ToString().ToLowerInvariant();

    public static string DisplayName(this PartyRole role) => role switch
    {
        PartyRole.T1 => "Tank 1",
        PartyRole.T2 => "Tank 2",
        PartyRole.H1 => "Healer 1",
        PartyRole.H2 => "Healer 2",
        PartyRole.M1 => "Melee 1",
        PartyRole.M2 => "Melee 2",
        PartyRole.R1 => "Ranged 1",
        PartyRole.R2 => "Ranged 2",
        _ => role.ToString(),
    };
}

