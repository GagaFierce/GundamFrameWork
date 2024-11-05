/**************************************************
 *
 * Copyright (c) 2024 WangJian 
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 * author       : WangJian
 * create date  : 2024 11 05
 * description  : Core
 *
***************************************************/ 
namespace Core.BehaviorTree.BHNode
{
    public interface  IBhNode
    {
        
    }
  
    public  interface  IBhFrameUpdateNode :IBhNode
    {
        public  void LogicFrameUpdate();
    }

    public class BhFrameUpdateNodeBase : BhNodeData,IBhFrameUpdateNode
    {
        public void LogicFrameUpdate()
        {
            throw new System.NotImplementedException();
        }
    }
 
    
  
    public interface  IBhStatedDisposeNode :IBhNode
    {
        public  void Enter();
        public  void Dispose();
        public void Exit();
    }

   

 
    public  class BhNodeData 
    {
       
    }
}
