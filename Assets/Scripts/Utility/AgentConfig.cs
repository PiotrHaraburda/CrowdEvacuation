using UnityEngine;

namespace Utility
{
    public static class AgentConfig
    {
        // Garcimartin et al. 2017: d=0.37m shoulder-to-shoulder
        public const float MeanRadius = 0.185f;
        private const float StdRadius = 0.015f;
        private const float MinRadius = 0.15f;
        public const float MaxRadius = 0.22f;

        // Weidmann 1993: desired walking speed 1.34 m/s +- 0.26 m/s
        private const float MeanDesiredSpeed = 1.34f;
        private const float StdDesiredSpeed = 0.26f;
        private const float MinDesiredSpeed = 0.5f;
        private const float MaxDesiredSpeed = 2.0f;

        // Helbing, Farkas & Vicsek 2000
        public const float Mass = 80f;

        // Helbing & Molnar 1995
        public const float VelocityClamp = 1.3f;
        public const float MaxSpeed = MeanDesiredSpeed * VelocityClamp; // 1.742 m/s
        public const float DefaultTau = 0.5f;
        public const float MaxForce = Mass * MeanDesiredSpeed / DefaultTau; // 214.4 N

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