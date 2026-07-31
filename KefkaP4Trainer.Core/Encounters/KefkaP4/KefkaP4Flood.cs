namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>
/// The fully resolved Flood of Naughts instruction for one party slot.
///
/// Source: <c>p4_seq.gd</c> assigns <c>black_safe_keys</c>/<c>white_safe_keys</c>
/// during Grand Cross 3 setup, then <c>flood_hit()</c> grades with
/// <c>if black_west == black_safe_keys.has(key): fail when pos.x &gt; 0</c>.
///
/// The assignment is deliberately not "match your wound colour". Waju's own
/// comment reads "If player has death Beyond Death, swap 'safe' side", so the
/// side depends on the wound colour, the Field/Death icon, and the fake flag
/// together:
///
/// <code>
/// black wound: black-safe when (hasBeyondDeath != floodFake)
/// white wound: black-safe when (hasBeyondDeath == floodFake)
/// </code>
///
/// which reduces to: real flood + Allagan Field takes the opposite colour,
/// real flood + Beyond Death takes the same colour, and a fake flood inverts
/// both. This record exists so the trainer can state that conclusion outright
/// rather than leaving the player to re-derive it under pressure.
/// </summary>
public sealed record FloodBriefing(
    bool FloodFake,
    bool BlackWest,
    bool HasBlackWound,
    bool HasBeyondDeath,
    bool StandWest)
{
    /// <summary>
    /// Which Antilight the resolved side is standing in. Equivalent to
    /// membership of Waju's <c>black_safe_keys</c>; see the parity test.
    /// </summary>
    public bool StandInBlack => StandWest == BlackWest;

    /// <summary>True when the safe Antilight matches the player's wound colour.</summary>
    public bool MatchesWoundColour => StandInBlack == HasBlackWound;

    public string SideText => StandWest ? "WEST" : "EAST";

    public string AntilightText => StandInBlack ? "Black" : "White";

    public string WoundText => HasBlackWound ? "Black" : "White";

    public string SecondaryText => HasBeyondDeath ? "Beyond Death" : "Allagan Field";

    /// <summary>Single-line instruction suitable for the on-screen cue.</summary>
    public string Instruction =>
        $"Flood {(FloodFake ? "Fake" : "Real")}: go {SideText} into {AntilightText} Antilight";

    /// <summary>Longer form that also shows why, for the debug window.</summary>
    public string Explanation =>
        $"{WoundText} Wound + {SecondaryText} + {(FloodFake ? "Fake" : "Real")} "
        + $"-> {(MatchesWoundColour ? "same" : "opposite")} colour "
        + $"-> {AntilightText} Antilight, which is {SideText} "
        + $"(Black is {(BlackWest ? "West" : "East")}).";
}
