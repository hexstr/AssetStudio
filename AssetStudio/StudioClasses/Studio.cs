﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static AssetStudio.Exporter;

namespace AssetStudio
{
    internal static class Studio
    {
        public static List<AssetsFile> assetsfileList = new List<AssetsFile>(); //loaded files
        public static Dictionary<string, int> sharedFileIndex = new Dictionary<string, int>(); //to improve the loading speed
        public static Dictionary<string, EndianBinaryReader> resourceFileReaders = new Dictionary<string, EndianBinaryReader>(); //use for read res files
        public static List<AssetPreloadData> exportableAssets = new List<AssetPreloadData>(); //used to hold all assets while the ListView is filtered
        private static HashSet<string> exportableAssetsHash = new HashSet<string>(); //avoid the same name asset
        public static List<AssetPreloadData> visibleAssets = new List<AssetPreloadData>(); //used to build the ListView from all or filtered assets

        public static string productName = "";
        public static string mainPath = "";
        public static List<GameObject> fileNodes = new List<GameObject>();

        public static Dictionary<string, Dictionary<string, string>> jsonMats;
        public static Dictionary<string, SortedDictionary<int, ClassStruct>> AllClassStructures = new Dictionary<string, SortedDictionary<int, ClassStruct>>();

        //UI
        public static Action<int> SetProgressBarValue;
        public static Action<int> SetProgressBarMaximum;
        public static Action ProgressBarPerformStep;
        public static Action<string> StatusStripUpdate;
        public static Action<int> ProgressBarMaximumAdd;

        public enum FileType
        {
            AssetsFile,
            BundleFile,
            WebFile
        }

        public static FileType CheckFileType(MemoryStream stream, out EndianBinaryReader reader)
        {
            reader = new EndianBinaryReader(stream);
            return CheckFileType(reader);
        }

        public static FileType CheckFileType(string fileName, out EndianBinaryReader reader)
        {
            reader = new EndianBinaryReader(File.OpenRead(fileName));
            return CheckFileType(reader);
        }

        private static FileType CheckFileType(EndianBinaryReader reader)
        {
            var signature = reader.ReadStringToNull();
            reader.Position = 0;
            switch (signature)
            {
                case "UnityWeb":
                case "UnityRaw":
                case "\xFA\xFA\xFA\xFA\xFA\xFA\xFA\xFA":
                case "UnityFS":
                    return FileType.BundleFile;
                case "UnityWebData1.0":
                    return FileType.WebFile;
                default:
                    {
                        var magic = reader.ReadBytes(2);
                        reader.Position = 0;
                        if (WebFile.gzipMagic.SequenceEqual(magic))
                        {
                            return FileType.WebFile;
                        }
                        reader.Position = 0x20;
                        magic = reader.ReadBytes(6);
                        reader.Position = 0;
                        if (WebFile.brotliMagic.SequenceEqual(magic))
                        {
                            return FileType.WebFile;
                        }
                        return FileType.AssetsFile;
                    }
            }
        }

        public static void ExtractFile(string[] fileNames)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                int extractedCount = 0;
                foreach (var fileName in fileNames)
                {
                    var type = CheckFileType(fileName, out var reader);
                    if (type == FileType.BundleFile)
                        extractedCount += ExtractBundleFile(fileName, reader);
                    else if (type == FileType.WebFile)
                        extractedCount += ExtractWebDataFile(fileName, reader);
                    else
                        reader.Dispose();
                    ProgressBarPerformStep();
                }
                StatusStripUpdate($"Finished extracting {extractedCount} files.");
            });
        }

        private static int ExtractBundleFile(string bundleFileName, EndianBinaryReader reader)
        {
            var bundleFile = new BundleFile(reader);
            reader.Dispose();
            if (bundleFile.fileList.Count > 0)
            {
                StatusStripUpdate($"Decompressing {Path.GetFileName(bundleFileName)} ...");
                var extractPath = bundleFileName + "_unpacked\\";
                Directory.CreateDirectory(extractPath);
                return ExtractMemoryFile(extractPath, bundleFile.fileList);
            }
            return 0;
        }

        private static int ExtractWebDataFile(string webFileName, EndianBinaryReader reader)
        {
            var webFile = new WebFile(reader);
            reader.Dispose();
            if (webFile.fileList.Count > 0)
            {
                StatusStripUpdate($"Decompressing {Path.GetFileName(webFileName)} ...");
                var extractPath = webFileName + "_unpacked\\";
                Directory.CreateDirectory(extractPath);
                return ExtractMemoryFile(extractPath, webFile.fileList);
            }
            return 0;
        }

        private static int ExtractMemoryFile(string extractPath, List<MemoryFile> fileList)
        {
            int extractedCount = 0;
            foreach (var memFile in fileList)
            {
                var filePath = extractPath + memFile.fileName;
                if (!Directory.Exists(extractPath))
                {
                    Directory.CreateDirectory(extractPath);
                }
                if (!File.Exists(filePath))
                {
                    File.WriteAllBytes(filePath, memFile.stream.ToArray());
                    memFile.stream.Dispose();
                    extractedCount += 1;
                }
            }
            return extractedCount;
        }

        public static void BuildAssetStructures(bool loadAssetsMenuItem, bool displayAll, bool buildHierarchyMenuItem, bool buildClassStructuresMenuItem, bool displayOriginalName)
        {
            #region first loop - read asset data & create list
            if (loadAssetsMenuItem)
            {
                SetProgressBarValue(0);
                SetProgressBarMaximum(assetsfileList.Sum(x => x.preloadTable.Values.Count));
                StatusStripUpdate("Building asset list...");

                string fileIDfmt = "D" + assetsfileList.Count.ToString().Length;

                for (var i = 0; i < assetsfileList.Count; i++)
                {
                    var assetsFile = assetsfileList[i];

                    string fileID = i.ToString(fileIDfmt);
                    AssetBundle ab = null;
                    foreach (var asset in assetsFile.preloadTable.Values)
                    {
                        asset.uniqueID = fileID + asset.uniqueID;
                        var exportable = false;
                        switch (asset.Type)
                        {
                            case ClassIDReference.GameObject:
                                {
                                    GameObject m_GameObject = new GameObject(asset);
                                    assetsFile.GameObjectList.Add(asset.m_PathID, m_GameObject);
                                    //totalTreeNodes++;
                                    break;
                                }
                            case ClassIDReference.Transform:
                                {
                                    Transform m_Transform = new Transform(asset);
                                    assetsFile.TransformList.Add(asset.m_PathID, m_Transform);
                                    break;
                                }
                            case ClassIDReference.RectTransform:
                                {
                                    RectTransform m_Rect = new RectTransform(asset);
                                    assetsFile.TransformList.Add(asset.m_PathID, m_Rect.m_Transform);
                                    break;
                                }
                            case ClassIDReference.Texture2D:
                                {
                                    Texture2D m_Texture2D = new Texture2D(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.Shader:
                                {
                                    Shader m_Shader = new Shader(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.TextAsset:
                                {
                                    TextAsset m_TextAsset = new TextAsset(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.AudioClip:
                                {
                                    AudioClip m_AudioClip = new AudioClip(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.MonoBehaviour:
                                {
                                    var m_MonoBehaviour = new MonoBehaviour(asset, false);
                                    if (asset.Type1 != asset.Type2 && assetsFile.ClassStructures.ContainsKey(asset.Type1))
                                        exportable = true;
                                    break;
                                }
                            case ClassIDReference.Font:
                                {
                                    UFont m_Font = new UFont(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.PlayerSettings:
                                {
                                    var plSet = new PlayerSettings(asset);
                                    productName = plSet.productName;
                                    break;
                                }
                            case ClassIDReference.Mesh:
                                {
                                    Mesh m_Mesh = new Mesh(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.AssetBundle:
                                {
                                    ab = new AssetBundle(asset);
                                    break;
                                }
                            case ClassIDReference.VideoClip:
                                {
                                    var m_VideoClip = new VideoClip(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.MovieTexture:
                                {
                                    var m_MovieTexture = new MovieTexture(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.Sprite:
                                {
                                    var m_Sprite = new Sprite(asset, false);
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.Animator:
                                {
                                    exportable = true;
                                    break;
                                }
                            case ClassIDReference.AnimationClip:
                                {
                                    exportable = true;
                                    var reader = asset.sourceFile.assetsFileReader;
                                    reader.Position = asset.Offset;
                                    asset.Text = reader.ReadAlignedString();
                                    break;
                                }
                        }
                        if (displayAll)
                        {
                            exportable = true;
                        }
                        if (exportable)
                        {
                            if (asset.Text == "")
                            {
                                asset.Text = asset.TypeString + " #" + asset.uniqueID;
                            }
                            asset.SubItems.AddRange(new[] { asset.TypeString, asset.fullSize.ToString() });
                            //处理同名文件
                            if (!exportableAssetsHash.Add((asset.TypeString + asset.Text).ToUpper()))
                            {
                                asset.Text += " #" + asset.uniqueID;
                            }
                            //处理非法文件名
                            asset.Text = FixFileName(asset.Text);
                            assetsFile.exportableAssets.Add(asset);
                        }
                        ProgressBarPerformStep();
                    }
                    if (displayOriginalName)
                    {
                        assetsFile.exportableAssets.ForEach(x =>
                        {
                            var replacename = ab?.m_Container.Find(y => y.second.asset.m_PathID == x.m_PathID)?.first;
                            if (!string.IsNullOrEmpty(replacename))
                            {
                                var ex = Path.GetExtension(replacename);
                                x.Text = !string.IsNullOrEmpty(ex) ? replacename.Replace(ex, "") : replacename;
                            }
                        });
                    }
                    exportableAssets.AddRange(assetsFile.exportableAssets);
                }

                visibleAssets = exportableAssets;
                exportableAssetsHash.Clear();
            }
            #endregion

            #region second loop - build tree structure
            fileNodes = new List<GameObject>();
            if (buildHierarchyMenuItem)
            {
                SetProgressBarMaximum(1);
                SetProgressBarValue(1);
                SetProgressBarMaximum(assetsfileList.Sum(x => x.GameObjectList.Values.Count) + 1);
                StatusStripUpdate("Building tree structure...");

                foreach (var assetsFile in assetsfileList)
                {
                    GameObject fileNode = new GameObject(null);
                    fileNode.Text = Path.GetFileName(assetsFile.filePath);
                    fileNode.m_Name = "RootNode";

                    foreach (var m_GameObject in assetsFile.GameObjectList.Values)
                    {
                        //ParseGameObject
                        foreach (var m_Component in m_GameObject.m_Components)
                        {
                            if (m_Component.m_FileID >= 0 && m_Component.m_FileID < assetsfileList.Count)
                            {
                                var sourceFile = assetsfileList[m_Component.m_FileID];
                                if (sourceFile.preloadTable.TryGetValue(m_Component.m_PathID, out var asset))
                                {
                                    switch (asset.Type)
                                    {
                                        case ClassIDReference.Transform:
                                            {
                                                m_GameObject.m_Transform = m_Component;
                                                break;
                                            }
                                        case ClassIDReference.MeshRenderer:
                                            {
                                                m_GameObject.m_MeshRenderer = m_Component;
                                                break;
                                            }
                                        case ClassIDReference.MeshFilter:
                                            {
                                                m_GameObject.m_MeshFilter = m_Component;
                                                break;
                                            }
                                        case ClassIDReference.SkinnedMeshRenderer:
                                            {
                                                m_GameObject.m_SkinnedMeshRenderer = m_Component;
                                                break;
                                            }
                                        case ClassIDReference.Animator:
                                            {
                                                m_GameObject.m_Animator = m_Component;
                                                asset.Text = m_GameObject.asset.Text;
                                                break;
                                            }
                                    }
                                }
                            }
                        }
                        //

                        var parentNode = fileNode;

                        if (assetsfileList.TryGetTransform(m_GameObject.m_Transform, out var m_Transform))
                        {
                            if (assetsfileList.TryGetTransform(m_Transform.m_Father, out var m_Father))
                            {
                                //GameObject Parent;
                                if (assetsfileList.TryGetGameObject(m_Father.m_GameObject, out parentNode))
                                {
                                    //parentNode = Parent;
                                }
                            }
                        }

                        parentNode.Nodes.Add(m_GameObject);
                        ProgressBarPerformStep();
                    }


                    if (fileNode.Nodes.Count == 0)
                    {
                        fileNode.Text += " (no children)";
                    }
                    fileNodes.Add(fileNode);
                }

                if (File.Exists(mainPath + "\\materials.json"))
                {
                    string matLine;
                    using (StreamReader reader = File.OpenText(mainPath + "\\materials.json"))
                    { matLine = reader.ReadToEnd(); }

                    jsonMats = new JavaScriptSerializer().Deserialize<Dictionary<string, Dictionary<string, string>>>(matLine);
                    //var jsonMats = new JavaScriptSerializer().DeserializeObject(matLine);
                }
            }
            #endregion

            #region build list of class strucutres
            if (buildClassStructuresMenuItem)
            {
                //group class structures by versionv
                foreach (var assetsFile in assetsfileList)
                {
                    if (AllClassStructures.TryGetValue(assetsFile.m_Version, out var curVer))
                    {
                        foreach (var uClass in assetsFile.ClassStructures)
                        {
                            curVer[uClass.Key] = uClass.Value;
                        }
                    }
                    else
                    {
                        AllClassStructures.Add(assetsFile.m_Version, assetsFile.ClassStructures);
                    }
                }
            }
            #endregion
        }

        public static string FixFileName(string str)
        {
            if (str.Length >= 260) return Path.GetRandomFileName();
            return Path.GetInvalidFileNameChars().Aggregate(str, (current, c) => current.Replace(c, '_'));
        }

        public static string[] ProcessingSplitFiles(List<string> selectFile)
        {
            var splitFiles = selectFile.Where(x => x.Contains(".split"))
                .Select(x => Path.GetDirectoryName(x) + "\\" + Path.GetFileNameWithoutExtension(x))
                .Distinct()
                .ToList();
            selectFile.RemoveAll(x => x.Contains(".split"));
            foreach (var file in splitFiles)
            {
                if (File.Exists(file))
                {
                    selectFile.Add(file);
                }
            }
            return selectFile.Distinct().ToArray();
        }

        public static void ExportAssets(string savePath, List<AssetPreloadData> toExportAssets, int assetGroupSelectedIndex, bool openAfterExport)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

                int toExport = toExportAssets.Count;
                int exportedCount = 0;

                SetProgressBarValue(0);
                SetProgressBarMaximum(toExport);
                foreach (var asset in toExportAssets)
                {
                    var exportpath = savePath + "\\";
                    if (assetGroupSelectedIndex == 1)
                    {
                        exportpath += Path.GetFileNameWithoutExtension(asset.sourceFile.filePath) + "_export\\";
                    }
                    else if (assetGroupSelectedIndex == 0)
                    {
                        exportpath = savePath + "\\" + asset.TypeString + "\\";
                    }
                    StatusStripUpdate($"Exporting {asset.TypeString}: {asset.Text}");
                    switch (asset.Type)
                    {
                        case ClassIDReference.Texture2D:
                            if (ExportTexture2D(asset, exportpath, true))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.AudioClip:
                            if (ExportAudioClip(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.Shader:
                            if (ExportShader(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.TextAsset:
                            if (ExportTextAsset(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.MonoBehaviour:
                            if (ExportMonoBehaviour(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.Font:
                            if (ExportFont(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.Mesh:
                            if (ExportMesh(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.VideoClip:
                            if (ExportVideoClip(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.MovieTexture:
                            if (ExportMovieTexture(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.Sprite:
                            if (ExportSprite(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        case ClassIDReference.Animator:
                            if (ExportAnimator(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;
                        default:
                            if (ExportRawFile(asset, exportpath))
                            {
                                exportedCount++;
                            }
                            break;

                    }
                    ProgressBarPerformStep();
                }

                var statusText = exportedCount == 0 ? "Nothing exported." : $"Finished exporting {exportedCount} assets.";

                if (toExport > exportedCount)
                {
                    statusText += $" {toExport - exportedCount} assets skipped (not extractable or files already exist)";
                }

                StatusStripUpdate(statusText);

                if (openAfterExport && exportedCount > 0)
                {
                    Process.Start(savePath);
                }
            });
        }

        public static void ExportSplitObjects(string savePath, TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                //遍历一级子节点
                foreach (TreeNode j in node.Nodes)
                {
                    //收集所有子节点
                    var gameObjects = new List<GameObject>();
                    CollectNode(j, gameObjects);
                    //跳过一些不需要导出的object
                    if (gameObjects.All(x => x.m_SkinnedMeshRenderer == null && x.m_MeshFilter == null))
                        continue;
                    //处理非法文件名
                    var filename = FixFileName(j.Text);
                    //每个文件存放在单独的文件夹
                    var targetPath = $"{savePath}{filename}\\";
                    //重名文件处理
                    for (int i = 1; ; i++)
                    {
                        if (Directory.Exists(targetPath))
                        {
                            targetPath = $"{savePath}{filename} ({i})\\";
                        }
                        else
                        {
                            break;
                        }
                    }
                    Directory.CreateDirectory(targetPath);
                    //导出FBX
                    StatusStripUpdate($"Exporting {filename}.fbx");
                    FBXExporter.WriteFBX($"{targetPath}{filename}.fbx", gameObjects);
                    StatusStripUpdate($"Finished exporting {filename}.fbx");
                }
                ProgressBarPerformStep();
            }
        }

        public static void ExportSplitObjectsNew(string savePath, TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                //遍历一级子节点
                foreach (TreeNode j in node.Nodes)
                {
                    //收集所有子节点
                    var gameObjects = new List<GameObject>();
                    CollectNode(j, gameObjects);
                    //跳过一些不需要导出的object
                    if (gameObjects.All(x => x.m_SkinnedMeshRenderer == null && x.m_MeshFilter == null))
                        continue;
                    //处理非法文件名
                    var filename = FixFileName(j.Text);
                    //每个文件存放在单独的文件夹
                    var targetPath = $"{savePath}{filename}\\";
                    //重名文件处理
                    for (int i = 1; ; i++)
                    {
                        if (Directory.Exists(targetPath))
                        {
                            targetPath = $"{savePath}{filename} ({i})\\";
                        }
                        else
                        {
                            break;
                        }
                    }
                    Directory.CreateDirectory(targetPath);
                    //导出FBX
                    StatusStripUpdate($"Exporting {j.Text}.fbx");
                    ExportGameObject((GameObject)j, targetPath);
                    StatusStripUpdate($"Finished exporting {j.Text}.fbx");
                }
                ProgressBarPerformStep();
            }
        }

        private static void CollectNode(TreeNode node, List<GameObject> gameObjects)
        {
            gameObjects.Add((GameObject)node);
            foreach (TreeNode i in node.Nodes)
            {
                CollectNode(i, gameObjects);
            }
        }

        public static void ExportAnimatorWithAnimationClip(AssetPreloadData animator, List<AssetPreloadData> animationList, string exportPath)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                StatusStripUpdate($"Exporting {animator.Text}");
                try
                {
                    ExportAnimator(animator, exportPath, animationList);
                    StatusStripUpdate($"Finished exporting {animator.Text}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"{ex.Message}\r\n{ex.StackTrace}");
                    StatusStripUpdate("Error in export");
                }
                ProgressBarPerformStep();
            });
        }

        public static void ExportObjectsWithAnimationClip(GameObject gameObject, string exportPath, List<AssetPreloadData> animationList = null)
        {
            StatusStripUpdate($"Exporting {gameObject.Text}");
            try
            {
                ExportGameObject(gameObject, exportPath, animationList);
                StatusStripUpdate($"Finished exporting {gameObject.Text}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex.Message}\r\n{ex.StackTrace}");
                StatusStripUpdate("Error in export");
            }
        }

        public static void ForeachTreeNodes(TreeNodeCollection nodes, string exportPath, Action<GameObject> action)
        {
            foreach (TreeNode i in nodes)
            {
                if (i.Checked)
                {
                    action((GameObject)i);
                }
                else
                {
                    ForeachTreeNodes(i.Nodes, exportPath, action);
                }
            }
        }
    }
}
