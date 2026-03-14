using System.Collections.Generic;

namespace Metrics
{
    [System.Serializable]
    public class AgentFrameRecord
    {
        public int agentId;
        public float time;
        public float posX;
        public float posZ;
        public float speed;
        public float localDensity;
    }

    [System.Serializable]
    public class CollisionRecord
    {
        public float time;
        public int agentIdA;
        public int agentIdB;
        public string type;
    }

    [System.Serializable]
    public class ThroughputRecord
    {
        public float time;
        public int agentId;
        public string exitId;
    }

    [System.Serializable]
    public class HeadwayRecord
    {
        public float time;
        public int agentId;
        public float headwayDistance;
        public float headwayVelocity;
    }
    
}