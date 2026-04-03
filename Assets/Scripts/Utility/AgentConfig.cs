using UnityEngine;

namespace Utility
{
    public static class AgentConfig
    {
        // Weidmann 1993 (Dreyfuss 1967): shoulder width 0.46m, 97.5th pctl 0.50m
        private const float MeanRadius = 0.23f;
        private const float StdRadius = 0.01f;
        private const float MinRadius = 0.19f;
        private const float MaxRadius = 0.27f;

        // Weidmann 1993: desired walking speed 1.34 m/s +- 0.26 m/s
        private const float MeanDesiredSpeed = 1.34f;
        private const float StdDesiredSpeed = 0.26f;
        private const float MinDesiredSpeed = 0.5f;
        private const float MaxDesiredSpeed = 2.0f;

        public static float SampleRadius()
        {
            return SampleGaussian(MeanRadius, StdRadius, MinRadius, MaxRadius);
        }

        public static float SampleDesiredSpeed()
        {
            return SampleGaussian(MeanDesiredSpeed, StdDesiredSpeed, MinDesiredSpeed, MaxDesiredSpeed);
        }

        private static float SampleGaussian(float mean, float std, float min, float max)
        {
            var u1 = Random.value;
            var u2 = Random.value;
            var z = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(u1, 1e-6f))) * Mathf.Cos(2f * Mathf.PI * u2);
            return Mathf.Clamp(mean + std * z, min, max);
        }
    }
}