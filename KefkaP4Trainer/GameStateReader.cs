using System.Numerics;
using KefkaP4Trainer.Core;

namespace KefkaP4Trainer;

internal sealed class GameStateReader
{
    private Vector3 previousWorldPosition;
    private bool hasPreviousPosition;

    public Vector3? LastWorldPosition { get; private set; }

    public float? LastGameRotation { get; private set; }

    public PlayerState Read(ArenaTransform arena, double elapsedSeconds)
    {
        var player = Services.ObjectTable.LocalPlayer;
        if (player is null)
        {
            hasPreviousPosition = false;
            LastWorldPosition = null;
            LastGameRotation = null;
            return PlayerState.Unavailable;
        }

        var world = player.Position;
        LastWorldPosition = world;
        LastGameRotation = player.Rotation;
        if (!arena.IsInitialized)
        {
            hasPreviousPosition = false;
            return PlayerState.Unavailable;
        }

        var speed = 0f;
        if (hasPreviousPosition && elapsedSeconds > 0.0001 && elapsedSeconds < 1)
        {
            var previous = new Vector2(previousWorldPosition.X, previousWorldPosition.Z);
            var current = new Vector2(world.X, world.Z);
            speed = Vector2.Distance(previous, current)
                * ArenaTransform.DmuScale
                / (float)elapsedSeconds;
        }

        previousWorldPosition = world;
        hasPreviousPosition = true;

        var arenaPosition = arena.WorldToSimulator(world);
        var worldFacing = ArenaTransform.FfxivForward(player.Rotation);
        var arenaFacing = arena.WorldDirectionToSimulator(worldFacing);
        return new PlayerState(true, arenaPosition, arenaFacing, speed);
    }

    public void ResetVelocitySample() => hasPreviousPosition = false;
}
