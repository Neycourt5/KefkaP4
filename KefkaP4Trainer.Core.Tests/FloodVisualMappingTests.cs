using System.Numerics;
using KefkaP4Trainer.Core;
using KefkaP4Trainer.Core.Encounters.KefkaP4;

namespace KefkaP4Trainer.Core.Tests;

/// <summary>
/// The full semantic-to-visual mapping for Flood of Naughts.
///
/// These tests exist because internal parity is not enough: the boolean rule can
/// match Waju exactly while the icon, the colour, the side, the drawn rectangle
/// and the graded rectangle disagree with one another. Every one of those links
/// is asserted here.
/// </summary>
public sealed class FloodVisualMappingTests
{
    private static FloodResolution Resolve(
        WoundType wound,
        SecondaryDebuffType secondary,
        FloodTruthState truth,
        ArenaSide blackSide,
        bool swapped = false) =>
        new()
        {
            Wound = wound,
            Secondary = secondary,
            Truth = truth,
            BlackAntilightSide = blackSide,
            SwappedColors = swapped,
        };

    // ---------------- the 16-row semantic table ----------------

    /// <summary>
    /// Allagan Field takes the opposite colour, Beyond Death takes the same
    /// colour, and a fake Flood inverts both. Asserted for both Black-side
    /// orientations, giving 16 rows.
    /// </summary>
    [Theory]
    // wound, secondary, truth, blackSide, expected antilight, expected side, same-colour
    [InlineData(WoundType.Black, SecondaryDebuffType.AllaganField, FloodTruthState.Real, ArenaSide.West, AntilightType.White, ArenaSide.East, false)]
    [InlineData(WoundType.Black, SecondaryDebuffType.AllaganField, FloodTruthState.Real, ArenaSide.East, AntilightType.White, ArenaSide.West, false)]
    [InlineData(WoundType.Black, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.West, AntilightType.Black, ArenaSide.West, true)]
    [InlineData(WoundType.Black, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.East, AntilightType.Black, ArenaSide.East, true)]
    [InlineData(WoundType.White, SecondaryDebuffType.AllaganField, FloodTruthState.Real, ArenaSide.West, AntilightType.Black, ArenaSide.West, false)]
    [InlineData(WoundType.White, SecondaryDebuffType.AllaganField, FloodTruthState.Real, ArenaSide.East, AntilightType.Black, ArenaSide.East, false)]
    [InlineData(WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.West, AntilightType.White, ArenaSide.East, true)]
    [InlineData(WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.East, AntilightType.White, ArenaSide.West, true)]
    // fake inverts every row above
    [InlineData(WoundType.Black, SecondaryDebuffType.AllaganField, FloodTruthState.Fake, ArenaSide.West, AntilightType.Black, ArenaSide.West, true)]
    [InlineData(WoundType.Black, SecondaryDebuffType.AllaganField, FloodTruthState.Fake, ArenaSide.East, AntilightType.Black, ArenaSide.East, true)]
    [InlineData(WoundType.Black, SecondaryDebuffType.BeyondDeath, FloodTruthState.Fake, ArenaSide.West, AntilightType.White, ArenaSide.East, false)]
    [InlineData(WoundType.Black, SecondaryDebuffType.BeyondDeath, FloodTruthState.Fake, ArenaSide.East, AntilightType.White, ArenaSide.West, false)]
    [InlineData(WoundType.White, SecondaryDebuffType.AllaganField, FloodTruthState.Fake, ArenaSide.West, AntilightType.White, ArenaSide.East, true)]
    [InlineData(WoundType.White, SecondaryDebuffType.AllaganField, FloodTruthState.Fake, ArenaSide.East, AntilightType.White, ArenaSide.West, true)]
    [InlineData(WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Fake, ArenaSide.West, AntilightType.Black, ArenaSide.West, false)]
    [InlineData(WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Fake, ArenaSide.East, AntilightType.Black, ArenaSide.East, false)]
    public void SemanticTruthTable(
        WoundType wound,
        SecondaryDebuffType secondary,
        FloodTruthState truth,
        ArenaSide blackSide,
        AntilightType expectedAntilight,
        ArenaSide expectedSide,
        bool expectedSameColour)
    {
        var resolution = Resolve(wound, secondary, truth, blackSide);

        Assert.Equal(expectedAntilight, resolution.RequiredAntilight);
        Assert.Equal(expectedSide, resolution.RequiredSide);
        Assert.Equal(expectedSameColour, resolution.SameColourRule);

        // The opposite of everything must actually be the opposite.
        Assert.NotEqual(resolution.RequiredAntilight, resolution.OppositeAntilight);
        Assert.NotEqual(resolution.RequiredSide, resolution.OppositeSide);
        Assert.NotEqual(resolution.RequiredColor, resolution.OppositeColor);
    }

    /// <summary>Black is purple and White is blue, unless deliberately swapped.</summary>
    [Theory]
    [InlineData(AntilightType.Black, false, FloodVisualColor.Purple)]
    [InlineData(AntilightType.White, false, FloodVisualColor.Blue)]
    [InlineData(AntilightType.Black, true, FloodVisualColor.Blue)]
    [InlineData(AntilightType.White, true, FloodVisualColor.Purple)]
    public void ColorMappingFollowsTheAntilightAndTheSwapFlag(
        AntilightType antilight,
        bool swapped,
        FloodVisualColor expected) =>
        Assert.Equal(expected, FloodColors.For(antilight, swapped));

    /// <summary>
    /// The two sides must always carry one purple and one blue, whichever way
    /// the pull is oriented and whether or not colours are swapped.
    /// </summary>
    [Fact]
    public void TheArenaAlwaysShowsExactlyOnePurpleAndOneBlueLine()
    {
        foreach (var blackSide in new[] { ArenaSide.West, ArenaSide.East })
        {
            foreach (var swapped in new[] { false, true })
            {
                var resolution = Resolve(
                    WoundType.Black, SecondaryDebuffType.BeyondDeath,
                    FloodTruthState.Real, blackSide, swapped);

                var west = resolution.ColorOn(ArenaSide.West);
                var east = resolution.ColorOn(ArenaSide.East);

                Assert.NotEqual(west, east);
                Assert.Contains(FloodVisualColor.Purple, new[] { west, east });
                Assert.Contains(FloodVisualColor.Blue, new[] { west, east });

                // ...and the named Antilights must sit on opposite sides too.
                Assert.NotEqual(
                    resolution.AntilightOn(ArenaSide.West),
                    resolution.AntilightOn(ArenaSide.East));
                Assert.Equal(
                    AntilightType.Black,
                    resolution.AntilightOn(blackSide));
            }
        }
    }

    /// <summary>The required side must actually carry the required colour.</summary>
    [Fact]
    public void RequiredSideCarriesTheRequiredColourInEveryCombination()
    {
        foreach (var wound in Enum.GetValues<WoundType>())
        foreach (var secondary in Enum.GetValues<SecondaryDebuffType>())
        foreach (var truth in Enum.GetValues<FloodTruthState>())
        foreach (var blackSide in Enum.GetValues<ArenaSide>())
        foreach (var swapped in new[] { false, true })
        {
            var resolution = Resolve(wound, secondary, truth, blackSide, swapped);

            Assert.Equal(resolution.RequiredColor, resolution.ColorOn(resolution.RequiredSide));
            Assert.Equal(resolution.RequiredAntilight, resolution.AntilightOn(resolution.RequiredSide));
            Assert.Equal(resolution.OppositeColor, resolution.ColorOn(resolution.OppositeSide));

            // The same/opposite phrasing must agree with the colours shown.
            Assert.Equal(
                resolution.SameColourRule,
                resolution.RequiredColor == resolution.WoundColor);
        }
    }

    // ---------------- grading ----------------

    [Theory]
    [InlineData(-20f, ArenaSide.West)]
    [InlineData(20f, ArenaSide.East)]
    [InlineData(-0.001f, ArenaSide.West)]
    [InlineData(0.001f, ArenaSide.East)]
    public void GradingDetectsTheSideFromNeoLocalX(float localX, ArenaSide expected)
    {
        var resolution = Resolve(
            WoundType.Black, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.West);
        var verdict = resolution.Grade(localX);

        Assert.Equal(expected, verdict.DetectedSide);
        Assert.Equal(resolution.ColorOn(expected), verdict.DetectedColor);
        Assert.Equal(expected == resolution.RequiredSide, verdict.Passed);
    }

    [Fact]
    public void StandingOnTheCentreLineFailsNeitherSide()
    {
        var resolution = Resolve(
            WoundType.White, SecondaryDebuffType.AllaganField, FloodTruthState.Fake, ArenaSide.East);

        var verdict = resolution.Grade(0f);

        Assert.True(verdict.OnCentreLine);
        Assert.Null(verdict.DetectedSide);
        Assert.True(verdict.Passed);
    }

    [Fact]
    public void FailureMessageNamesBothTheNeededAndTheTakenColour()
    {
        var resolution = Resolve(
            WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.East);

        // Required is White = BLUE on the WEST; stand east instead.
        var verdict = resolution.Grade(20f);

        Assert.False(verdict.Passed);
        Assert.NotNull(verdict.FailureReason);
        Assert.Contains("BLUE", verdict.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("WEST", verdict.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("PURPLE", verdict.FailureReason!, StringComparison.Ordinal);
        Assert.Contains("EAST", verdict.FailureReason!, StringComparison.Ordinal);
    }

    /// <summary>The cue must lead with what the player can see.</summary>
    [Fact]
    public void CueLeadsWithSideAndColourAndKeepsTheCanonicalName()
    {
        var resolution = Resolve(
            WoundType.White, SecondaryDebuffType.BeyondDeath, FloodTruthState.Real, ArenaSide.East);

        Assert.Equal("Flood REAL: WEST - BLUE line (White Antilight)", resolution.Cue);
    }

    // ---------------- the reported screenshot ----------------

    /// <summary>
    /// The exact state from the reported in-game screenshot: Flood REAL, Black
    /// East, White Wound, Beyond Death.
    ///
    /// White Wound + Beyond Death + Real is a SAME-colour case, so the answer is
    /// the White Antilight, which is BLUE, and with Black on the East that puts
    /// it on the WEST. The player reported reading their wound icon as purple
    /// and therefore taking the purple line; the same-colour reasoning was
    /// right, but the wound icon was misread. Nothing about the rule was wrong,
    /// which is why the fix is presentational: the cue now names the visible
    /// colour and side first instead of an internal Black/White label.
    /// </summary>
    [Fact]
    public void ReportedWhiteWoundBeyondDeathRealBlackEastCase()
    {
        var resolution = Resolve(
            WoundType.White,
            SecondaryDebuffType.BeyondDeath,
            FloodTruthState.Real,
            ArenaSide.East);

        Assert.True(resolution.SameColourRule);
        Assert.Equal(AntilightType.White, resolution.RequiredAntilight);
        Assert.Equal(FloodVisualColor.Blue, resolution.RequiredColor);
        Assert.Equal(ArenaSide.West, resolution.RequiredSide);

        // The wound itself is blue, so "same colour as my wound" also lands blue.
        Assert.Equal(FloodVisualColor.Blue, resolution.WoundColor);

        // Standing in the purple line, as reported, is a fail.
        var purpleSide = resolution.ColorOn(ArenaSide.West) == FloodVisualColor.Purple
            ? ArenaSide.West
            : ArenaSide.East;
        Assert.Equal(ArenaSide.East, purpleSide);
        Assert.False(resolution.Grade(20f).Passed);
        Assert.True(resolution.Grade(-20f).Passed);
    }

    /// <summary>
    /// The player's own reading of the screenshot: a purple wound and a purple
    /// person, therefore "take purple". If the wound really were the purple
    /// (Black) one, that reasoning would send them EAST and would be correct.
    /// This pins the fact that the visible-colour logic is sound and the failure
    /// was in reading which wound was held.
    /// </summary>
    [Fact]
    public void ReportedCaseAsThePlayerReadItWouldHaveBeenPurpleEast()
    {
        var asPlayerRead = Resolve(
            WoundType.Black,
            SecondaryDebuffType.BeyondDeath,
            FloodTruthState.Real,
            ArenaSide.East);

        Assert.True(asPlayerRead.SameColourRule);
        Assert.Equal(FloodVisualColor.Purple, asPlayerRead.WoundColor);
        Assert.Equal(FloodVisualColor.Purple, asPlayerRead.RequiredColor);
        Assert.Equal(ArenaSide.East, asPlayerRead.RequiredSide);
        Assert.True(asPlayerRead.Grade(20f).Passed);
    }

    // ---------------- stage geometry ----------------

    /// <summary>
    /// Neo sits on the arena edge and the two banners straddle it, so the banner
    /// on a side must un-rotate to that side's sign - the same frame the grader
    /// uses.
    /// </summary>
    [Fact]
    public void AntilightBannersSitOnTheSideTheyClaimAtEveryRotation()
    {
        for (var step = 0; step < 8; step++)
        {
            var rotation = step * 45f;

            var neo = FloodStage.NeoPosition(rotation);
            Assert.Equal(
                FloodStage.NeoDistance,
                neo.Length(),
                3);

            var west = Geometry.RotateDegrees(
                FloodStage.AntilightPosition(ArenaSide.West, rotation), -rotation);
            var east = Geometry.RotateDegrees(
                FloodStage.AntilightPosition(ArenaSide.East, rotation), -rotation);

            Assert.True(west.X < 0, $"west banner at rotation {rotation} had X {west.X}");
            Assert.True(east.X > 0, $"east banner at rotation {rotation} had X {east.X}");
        }
    }

    /// <summary>
    /// The drawn rectangle and the graded region must be the same side. This is
    /// the failure the whole typed model exists to prevent.
    /// </summary>
    [Fact]
    public void DrawnHalvesAndGradedRegionAgreeForEverySeedAndRole()
    {
        for (long seed = 0; seed < 120; seed++)
        {
            foreach (var role in PartyRoles.All)
            {
                var encounter = new KefkaP4Encounter(seed, role);
                var resolution = encounter.FloodResolutionFor(role);
                var rotation = encounter.Assignments.NeoRotationDegrees;

                _ = encounter.ProcessEvent(
                    new TimelineEvent(49.7, 0, "CastFlood", TimelineEventKind.CastFlood),
                    PlayerState.Unavailable, 1, evaluate: false);

                var halves = encounter.AllShapes
                    .Where(shape => shape.Label.Contains("Antilight", StringComparison.Ordinal))
                    .ToList();
                Assert.Equal(2, halves.Count);

                // Exactly one purple and one blue, every pull.
                Assert.Single(halves, h => h.Label.StartsWith("PURPLE", StringComparison.Ordinal));
                Assert.Single(halves, h => h.Label.StartsWith("BLUE", StringComparison.Ordinal));

                var requiredLabel = $"{resolution.RequiredColor.ToString().ToUpperInvariant()} "
                    + $"{resolution.RequiredAntilight} Antilight "
                    + $"({resolution.RequiredSide.ToString().ToUpperInvariant()})";
                var required = Assert.Single(
                    halves,
                    h => string.Equals(h.Label, requiredLabel, StringComparison.Ordinal));

                // The drawn rectangle's own centre must un-rotate to the side it names.
                var centre = required.Origin + (required.Direction * (required.Length / 2));
                var local = Geometry.RotateDegrees(centre, -rotation);
                var drawnSide = local.X < 0 ? ArenaSide.West : ArenaSide.East;
                Assert.Equal(resolution.RequiredSide, drawnSide);

                // ...and standing inside it must pass the grader.
                var inside = Geometry.RotateDegrees(
                    new Vector2(resolution.RequiredSide == ArenaSide.West ? -20f : 20f, 0),
                    rotation);
                var result = encounter.ProcessEvent(
                    new TimelineEvent(55.0, 0, "ResolveFlood", TimelineEventKind.ResolveFlood),
                    new PlayerState(true, inside, new Vector2(0, -1), 0),
                    1,
                    evaluate: true);
                Assert.True(result!.Passed, $"seed {seed} {role}: {result.Reason}");
            }
        }
    }
}
