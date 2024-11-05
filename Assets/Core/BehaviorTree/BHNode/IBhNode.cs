/*****************************************************************************
 * 
 * author       : Wangjian
 * create date  : 2024 10 18
 * description  : IBhNode
 * 
 *****************************************************************************/
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