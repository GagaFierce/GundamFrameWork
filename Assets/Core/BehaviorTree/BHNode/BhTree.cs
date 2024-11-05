/**************************************************
 *
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
using System.Collections.Generic;

namespace Core.BehaviorTree.BHNode
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
