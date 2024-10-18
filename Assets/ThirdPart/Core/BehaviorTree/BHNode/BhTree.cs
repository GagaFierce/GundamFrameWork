using System.Collections.Generic;

namespace ThirdPart.Core.BehaviorTree.BhNode
{
    public class BhTree :IBhNode
    {
        private BhNodeData mBhNodeData;
        private List<IBhFrameUpdateNode> mBhFrameUpdateNodes;

        BhTree()
        {
            mBhFrameUpdateNodes = new List<IBhFrameUpdateNode>();
        }
        
       
    }
} 