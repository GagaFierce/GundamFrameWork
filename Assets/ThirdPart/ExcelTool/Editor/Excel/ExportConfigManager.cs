using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExportTool
{
    public class ExportConfigManager
    {
        string cfgMgrUp = @"
using System;
using System.Collections.Generic;
namespace Config{
    public class ConfigFactory{
        private static ConfigFactory sInstance = null;
        public static ConfigFactory Instance{
            get{
                if (sInstance == null){
                    sInstance = new ConfigFactory();
                    sInstance.Init();
                }
                return sInstance;
            }
        }
        ";
        string cfgMgrdown = @"
    }
}
        ";

        List<string> configNames;
        string fullPathName;

        string tempFile;
        public ExportConfigManager(string pFullPath, List<string> pNamses)
        {
            fullPathName = pFullPath;
            configNames = pNamses;

            tempFile = fullPathName + ".temp";
        }

        public void StartExport()
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                using (FileStream tfile = File.OpenWrite(tempFile))
                {
                    ExportTool.TextWriter twt = new ExportTool.TextWriter(tfile);

                    twt.WriteLine(cfgMgrUp);
                    twt.Indent().Indent();

                    twt.Indent().Indent();
                    twt.WriteLine("public void Init(bool clearCache = false){");
                    twt.Indent();
                    foreach (string tcfg in configNames)
                    {
                        twt.WriteLine($"Conf{tcfg}.Init(clearCache);");
                    }
                    twt.Outdent();
                    twt.WriteLine("}");
                    twt.Outdent().Outdent();
                    
                    
                    twt.WriteLine(cfgMgrdown);
                    twt.Close();
                    tfile.Close();

                    if (File.Exists(fullPathName))
                        File.Delete(fullPathName);
                    File.Move(tempFile, fullPathName);
                }
            }
            catch (Exception pErro)
            {
                Debug.LogError(pErro.ToString());
            }

        }
    }
}
