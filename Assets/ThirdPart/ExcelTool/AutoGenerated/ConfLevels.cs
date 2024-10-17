using System.Collections.Generic;
using ThirdPart.Core.ResLoad;

namespace Config{
    /// Generated from Levels.xlsx
    public class ConfLevels {
        /// 关卡ID
        public readonly int id;
        /// 进关消耗
        public readonly int levelSpend;
        /// 切割宽高
        public readonly int widthHeight;
        /// 目标数量
        public readonly int[] targetCount;
        /// 收集颜色
        public readonly int[] collectColors;
        /// 步数
        public readonly int step;
        /// 棋盘颜色
        public readonly int[] mapColors;
        /// 棋盘颜色出现概率
        public readonly float[] mapColorsWeight;
        /// 目标图片
        public readonly string[] targePic;
        /// 爆金币倍率
        public readonly float[] coinProbability;
        /// 自动填充
        public readonly int[] autoFill;
        /// 箱子道具
        public readonly int[] itemForBoxList;
        /// 每局积分
        public readonly int cost;
        public ConfLevels(System.IO.BinaryReader _reader){
            id = _reader.ReadInt32();
            levelSpend = _reader.ReadInt32();
            widthHeight = _reader.ReadInt32();
            
            int tcounttargetCount = _reader.ReadInt32();
            targetCount = new int[tcounttargetCount];
            for(int i=0; i< tcounttargetCount; i++){
                targetCount[i] = _reader.ReadInt32();
            }
            
            
            int tcountcollectColors = _reader.ReadInt32();
            collectColors = new int[tcountcollectColors];
            for(int i=0; i< tcountcollectColors; i++){
                collectColors[i] = _reader.ReadInt32();
            }
            
            step = _reader.ReadInt32();
            
            int tcountmapColors = _reader.ReadInt32();
            mapColors = new int[tcountmapColors];
            for(int i=0; i< tcountmapColors; i++){
                mapColors[i] = _reader.ReadInt32();
            }
            
            
            int tcountmapColorsWeight = _reader.ReadInt32();
            mapColorsWeight = new float[tcountmapColorsWeight];
            for(int i=0; i< tcountmapColorsWeight; i++){
                mapColorsWeight[i] = _reader.ReadSingle();
            }
            
            
            int tcounttargePic = _reader.ReadInt32();
            targePic = new string[tcounttargePic];
            for(int i=0; i< tcounttargePic; i++){
                targePic[i] = _reader.ReadString();
            }
            
            
            int tcountcoinProbability = _reader.ReadInt32();
            coinProbability = new float[tcountcoinProbability];
            for(int i=0; i< tcountcoinProbability; i++){
                coinProbability[i] = _reader.ReadSingle();
            }
            
            
            int tcountautoFill = _reader.ReadInt32();
            autoFill = new int[tcountautoFill];
            for(int i=0; i< tcountautoFill; i++){
                autoFill[i] = _reader.ReadInt32();
            }
            
            
            int tcountitemForBoxList = _reader.ReadInt32();
            itemForBoxList = new int[tcountitemForBoxList];
            for(int i=0; i< tcountitemForBoxList; i++){
                itemForBoxList[i] = _reader.ReadInt32();
            }
            
            cost = _reader.ReadInt32();
        }
        private static List<ConfLevels>  cacheArray = new List<ConfLevels>();
        public static List<ConfLevels> array 
        {
            get
            {
                GetArrrayList();
                return cacheArray;
            }
        }
        public static void Init(bool clearCache = false)
        {
            if (clearCache)
                cacheArray.Clear();
            GetArrrayList();
        }
        private static Dictionary<int, ConfLevels> dic = new Dictionary<int, ConfLevels>();
        public static ConfLevels Get(int id)
        {
            if (cacheArray.Count <= 0)
            {
                GetArrrayList();
            }

            ConfLevels config;
            if (dic.TryGetValue(id, out config))
            {
                return config;
            }

            return null;
        }

        private static void GetArrrayList()
        {
            if (cacheArray.Count <= 0)
            {
                UnityEngine.TextAsset textAsset =
                    ResourcesManagerImpl.Instance.CreateText("ConfigBytes/ConfLevels");
                if (!textAsset) return;
                byte[] tbys = textAsset.bytes;
                if (tbys == null) return;
                var treader = new System.IO.BinaryReader(new System.IO.MemoryStream(tbys));
                int trow = treader.ReadInt32();
                for (int i = 0; i < trow; i++)
                {
                    ConfLevels _conf = new ConfLevels(treader);
                    cacheArray.Add(_conf);
                    dic[_conf.id] = _conf;
                }

                treader.Close();
            }
        }
    }
}
