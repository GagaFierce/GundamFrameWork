using System;
using System.Collections.Generic;

namespace ThirdPart.Core.BehaviorTree.BhNode.BhSearcNode
{
    public class BhSequenceNode : IBhFrameUpdateNode
    {
        private List<IBhStatedDisposeNode> mIBhNodeList;

         BhSequenceNode()
        {
            mIBhNodeList = new List<IBhStatedDisposeNode>();
        }

        public void AddIBhNode(IBhStatedDisposeNode iBhNode)
        {
            if (!mIBhNodeList.Contains(iBhNode))
            {
                mIBhNodeList.Add(iBhNode);
            }
            else
            {
                Console.Write("BhSequenceNode添加节点失败");
            }
        }
        public void LogicFrameUpdate()
        {
            foreach (var bH in mIBhNodeList)
            {
                bH.Enter();
            }
        }
    }
}