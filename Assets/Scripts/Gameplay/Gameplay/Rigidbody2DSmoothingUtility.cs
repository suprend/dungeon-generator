using UnityEngine;

public static class Rigidbody2DSmoothingUtility
{
    public static void EnableInterpolation(Rigidbody2D body2D)
    {
        if (body2D == null || body2D.bodyType == RigidbodyType2D.Static)
            return;

        body2D.interpolation = RigidbodyInterpolation2D.Interpolate;
    }
}
