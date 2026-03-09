using System;

namespace Agents
{
    [Serializable]
    public class AgentTrajectoryData
    {
        public int id;
        public float[] x;
        public float[] y;
        public int[] frame;
        public float[] t;
    }

    [Serializable]
    public class EpisodeData
    {
        public AgentTrajectoryData[] agents;
    }
}