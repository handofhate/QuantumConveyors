using MoonFramework;
using UnityEngine;

namespace QuantumConveyors;

internal static class EndpointAssets
{
    public static readonly ModelSpec OutputModel = new()
    {
        File = "quantum-output.obj",
        CollisionFile = "quantum-endpoint-collision.obj",
        Offset = new Vector3(0f, 0.85f, 1.85f),
        Turn = 90f,
        HideBody = true,
        KeepBodyColliders = false,
        Parts = new[] { "Indicator" },
    };

    public static readonly ModelSpec InputModel = new()
    {
        File = "quantum-input.obj",
        CollisionFile = "quantum-endpoint-collision.obj",
        Offset = new Vector3(0f, 0.85f, 2.15f),
        Turn = 90f,
        HideBody = true,
        KeepBodyColliders = false,
        Parts = new[] { "Indicator" },
    };
}
