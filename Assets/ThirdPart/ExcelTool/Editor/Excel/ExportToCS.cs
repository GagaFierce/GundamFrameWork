using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ExportTool
{
    public class ExportToCS
    {
        string filename;
        string tempFile;
        string className;
        ExcelData data;

        List<string> fieldList = new List<string>();
        public List<string> GetFieldList() { return fieldList; }

        public ExportToCS(string pName,string pFullPath, ExcelData pData)
        {
            className = pName;
            filename = pFullPath;
            data = pData;

            tempFile = filename + ".temp";
        }
        public bool StartExport()
        {
            FileStream tfile = null;
            TextWriter twt = null;

            UnityEngine.Debug.Log($"ExportClass: {className}, startc = {data.startC}");
            try
            {
                var tfirstTypeStr = data.objects[ExcelData.sTypeLine, data.startC];
                var tfirstNameStr = data.objects[ExcelData.sFieldNameLine, data.startC];
                var noteStr = data.objects[ExcelData.sContext, data.startC];

                if (string.IsNullOrEmpty(tfirstTypeStr) || string.IsNullOrEmpty(tfirstNameStr))
                {
                    ShowError($"ExportClass Error: {className}, startc = {data.startC},TypeStr = {tfirstTypeStr}, NameStr = {tfirstNameStr}");
                    return false;
                }

                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                tfile = File.OpenWrite(tempFile);
                twt = new TextWriter(tfile);
                

                //twt.WriteLine("using Utils;");
                twt.WriteLine("using System.Collections.Generic;");
                twt.WriteLine("namespace Config{").Indent();
                twt.WriteLine($"/// Generated from {data.name}.xlsx");
                twt.WriteLine($"public class {className} {{").Indent();

                //new class
                for (int i = data.startC; i < data.c; i++)
                {
                    if (!data.IsNeed(i)) continue;
                    var ttypeStr = data.objects[ExcelData.sTypeLine, i];
                    var tnameStr = data.objects[ExcelData.sFieldNameLine, i];
                    fieldList.Add(tnameStr);

                    if (string.IsNullOrEmpty(ttypeStr) || string.IsNullOrEmpty(tnameStr))
                    {
                        throw new Exception($"第{i}列字段错误 type={ttypeStr}, fieldName={tnameStr}");
                    }
                    string _noteStr = data.objects[ExcelData.sContext, i];
                    
                    twt.WriteLine($"/// {_noteStr.Replace("\r\n", "").Replace("\n", "")}");
                    twt.WriteLine($"public readonly {ttypeStr} {tnameStr};");
                }
                twt.WriteLine($"public {className}(System.IO.BinaryReader _reader){{").Indent();
                for (int i = data.startC; i < data.c; i++)
                {
                    if (!data.IsNeed(i)) continue;
                    var ttypeStr = data.objects[ExcelData.sTypeLine, i];
                    var tnameStr = data.objects[ExcelData.sFieldNameLine, i];


                    if (string.IsNullOrEmpty(ttypeStr) || string.IsNullOrEmpty(tnameStr))
                    {
                        throw new Exception($"第{i}列字段错误 type={ttypeStr}, fieldName={tnameStr}");
                    }

                    WriteReadStr(twt, ttypeStr, tnameStr);
                }
                twt.Outdent().WriteLine("}");
                //twt.Outdent().WriteLine("}");
                
                //private static List<ConfAllianceChatEmoticons>  cacheArray = new List<ConfAllianceChatEmoticons>();
                twt.WriteLine($"private static List<{className}>  cacheArray = new List<{className}>();");
                // public static List<ConfAllianceChatEmoticons> array 
                // {
                //     get
                //     {
                //         GetArrrayList();
                //         return cacheArray;
                //     }
                // }
                twt.WriteLine($"public static List<{className}> array ");
                twt.WriteLine("{").Indent();
                twt.WriteLine("get");
                twt.WriteLine("{").Indent();
                twt.WriteLine("GetArrrayList();");
                twt.WriteLine("return cacheArray;");
                twt.Outdent().WriteLine("}").Outdent();
                twt.WriteLine("}");
                // public static void Init()
                // {
                //     GetArrrayList();
                // }
                twt.WriteLine($"public static void Init(bool clearCache = false)");
                twt.WriteLine("{").Indent();
                twt.WriteLine("if (clearCache)").Indent();
                twt.WriteLine("cacheArray.Clear();").Outdent();
                twt.WriteLine("GetArrrayList();");
                twt.Outdent().WriteLine("}");
                //    private static Dictionary<int, ConfAllianceChatEmoticons> dic = new Dictionary<int, ConfAllianceChatEmoticons>();
                twt.WriteLine($"private static Dictionary<{tfirstTypeStr}, {className}> dic = new Dictionary<{tfirstTypeStr}, {className}>();");
                // public static ConfAllianceChatEmoticons Get(int id)
                // {
                //     ConfAllianceChatEmoticons config;
                //     if (dic.TryGetValue(id, out config))
                //     {
                //         return config;
                //     }
                //
                //     return null;
                // }
                twt.WriteLine($"public static {className} Get({tfirstTypeStr} id)");
                twt.WriteLine("{").Indent();
                twt.WriteLine($"if (cacheArray.Count <= 0)");
                twt.WriteLine("{").Indent();
                twt.WriteLine(" GetArrrayList();");
                twt.Outdent().WriteLine("}");
                twt.WriteLine($"{className} config;");
                twt.WriteLine($"if (dic.TryGetValue(id, out config))");
                twt.WriteLine("{").Indent();
                twt.WriteLine("return config;");
                twt.Outdent().WriteLine("}");
                twt.WriteLine("return null;");
                twt.Outdent().WriteLine("}");
                
                // private static void GetArrrayList()
                // {
                //     if(cacheArray.Count <= 0)
                //     {
                //         byte[] tbys = LitEngine.LoaderManager.LoadConfigFile("AllianceChatEmoticons.bytes");
                //         if (tbys == null) return;
                //         var treader = new System.IO.BinaryReader(new System.IO.MemoryStream(tbys));
                //         int trow = treader.ReadInt32();
                //         for (int i = 0; i < trow; i++){
                //             ConfAllianceChatEmoticons _conf = new ConfAllianceChatEmoticons(treader);
                //             cacheArray.Add(_conf);
                //             dic[_conf.id] = _conf;
                //         }
                //         treader.Close();
                //     }
                // }
                
                
                twt.WriteLine($"private static void GetArrrayList()").Indent();
                twt.WriteLine("{").Indent();
                twt.WriteLine("if(cacheArray.Count <= 0)").Indent();
                twt.WriteLine("{").Indent();

                twt.WriteLine($"UnityEngine.TextAsset textAsset = ExcelTool.Script.ResourcesManager.Instance.CreateText(\"ConfigBytes/{className}\");");
                twt.WriteLine("if (!textAsset) return;");
                twt.WriteLine("byte[] tbys = textAsset.bytes;");
                twt.WriteLine("if (tbys == null) return;");
                twt.WriteLine("var treader = new System.IO.BinaryReader(new System.IO.MemoryStream(tbys));");
                twt.WriteLine("int trow = treader.ReadInt32();");
                twt.WriteLine("for (int i = 0; i < trow; i++){").Indent();
                twt.WriteLine($"{className} _conf = new {className}(treader);");
                twt.WriteLine("cacheArray.Add(_conf);");
                twt.WriteLine($"dic[_conf.{tfirstNameStr}] = _conf;");
                twt.Outdent().WriteLine("}");
                twt.WriteLine("treader.Close();");
                twt.Outdent().WriteLine("}");
                twt.Outdent().WriteLine("}");
                

                

                twt.Outdent().WriteLine("}");
                twt.Outdent().WriteLine("}");
                twt?.Close();
                tfile?.Close();

                if (File.Exists(filename))
                    File.Delete(filename);
                File.Move(tempFile, filename);

                return true;

            }
            catch (Exception ex)
            {
                twt?.Close();
                tfile?.Close();
                ShowError(ex.Message);
            }

            return false;
        }
        public void WriteReadStr(TextWriter _writer, string _typestr, string _valuename)
        {
            if (_typestr.Contains("[]"))
            {
                string tctype = _typestr.Replace("[]", "");
                _writer.WriteLine("");
                _writer.WriteLine($"int tcount{_valuename} = _reader.ReadInt32();");
                _writer.WriteLine($"{_valuename} = new {tctype}[tcount{_valuename}];");
                _writer.WriteLine($"for(int i=0; i< tcount{_valuename}; i++){"{"}").Indent();
                WriteReadStr(_writer, tctype, _valuename + "[i]");
                _writer.Outdent().WriteLine("}");
                _writer.WriteLine("");
            }
            else
            {
                switch (_typestr)
                {
                    case "int":
                        _writer.WriteLine($"{_valuename} = _reader.ReadInt32();");
                        break;
                    case "float":
                        _writer.WriteLine($"{_valuename} = _reader.ReadSingle();");
                        break;
                    case "string":
                        _writer.WriteLine($"{_valuename} = _reader.ReadString();");
                        break;
                    case "long":
                        _writer.WriteLine($"{_valuename} = _reader.ReadInt64();");
                        break;
                    case "byte":
                        _writer.WriteLine($"{_valuename} = _reader.ReadByte();");
                        break;
                    case "short":
                        _writer.WriteLine($"{_valuename} = _reader.ReadInt16();");
                        break;
                    case "bool":
                        _writer.WriteLine($"{_valuename} = _reader.ReadBoolean();");
                        break;
                }
            }

        }


        void ShowError(string pErro)
        {
            if (UnityEditor.EditorUtility.DisplayDialog("Error", $"表 {filename} 生成配置出现错误.erro = {pErro}", "ok"))
            {
                Debug.LogError($"表 {filename} 生成配置出现错误.erro = {pErro}");
            }
        }
    }
}
