namespace KefkaP4Trainer.Core.Encounters.KefkaP4;

/// <summary>
/// The real/fake state Kefka is advertising for the cast currently on screen.
/// </summary>
/// <remarks>
/// In the encounter this is read off the orbs on the rings around Kefka: an orb
/// carrying a question mark means that element resolves as the opposite of the
/// orientation its telegraph shows. Thunder (the lines) and ice (the cones) are
/// flagged independently, so a cast can be fake in one element and real in the
/// other. Some casts advertise only one element, hence the Has* flags.
/// </remarks>
public readonly record struct MagicTell(
    string Label,
    bool HasThunder,
    bool ThunderFake,
    bool HasIce,
    bool IceFake,
    string CueText);
