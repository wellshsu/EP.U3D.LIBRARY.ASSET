//---------------------------------------------------------------------//
//                    GNU GENERAL PUBLIC LICENSE                       //
//                       Version 2, June 1991                          //
//                                                                     //
// Copyright (C) Wells Hsu, wellshsu@outlook.com, All rights reserved. //
// Everyone is permitted to copy and distribute verbatim copies        //
// of this license document, but changing it is not allowed.           //
//                  SEE LICENSE.md FOR MORE DETAILS.                   //
//---------------------------------------------------------------------//
using EP.U3D.LIBRARY.BASE;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace EP.U3D.LIBRARY.ASSET
{
    /// <summary>
    /// 资源加载管理器
    /// </summary>
    public static class AssetManager
    {
        #region internal class | 内部类
        /// <summary>
        /// 资源加载回调
        /// </summary>
        /// <param name="obj"></param>
        public delegate void Callback(UnityEngine.Object asset);

        /// <summary>
        /// 异步加载句柄（若同时加载同一资源，可能会出现progress不同步的问题）
        /// </summary>
        public class Handler
        {
            public int DoneCount; // 完成的数量（仅AssetBundle模式）
            public int TotalCount; // 总共的数量（资源*1 + 主Bundle*1 + 依赖Bundle*n）（仅AssetBundle模式）

            /// <summary>
            /// 加载进度
            /// </summary>
            public float Progress
            {
                get
                {
                    if (TotalCount == 0 || TotalCount == 1) // Resources模式 || AssetBundle模式，但是被其他任务batch了
                    {
                        if (Operation != null)
                        {
                            return Operation.progress;
                        }
                        else
                        {
                            return 0f;
                        }
                    }
                    else
                    {
                        if (Operation != null) // 最后的加载阶段
                        {
                            return (DoneCount + Operation.progress) / TotalCount;
                        }
                        else
                        {
                            return DoneCount / (float)TotalCount;
                        }
                    }
                }
            }

            /// <summary>
            /// 即将加载
            /// </summary>
            public event Action WillLoad;

            /// <summary>
            /// 加载完成
            /// </summary>
            public event Action AfterLoad;

            /// <summary>
            /// 加载操作
            /// </summary>
            public AsyncOperation Operation;

            public void DoWillLoad()
            {
                WillLoad?.Invoke();
            }

            public void DoAfterLoad()
            {
                AfterLoad?.Invoke();
            }
        }

        /// <summary>
        /// 场景任务（批处理）
        /// </summary>
        public class SceneTask
        {
            public string Name;
            public AsyncOperation Req;
        }

        /// <summary>
        /// Asset任务（批处理）
        /// </summary>
        public class AssetTask
        {
            public string Name;
            public AsyncOperation Req;
        }

        /// <summary>
        /// AB任务（批处理）
        /// </summary>
        public class BundleTask
        {
            public string Name;
            public AssetBundleCreateRequest Req;
        }

        /// <summary>
        /// AB信息
        /// </summary>
        public class BundleInfo
        {
            public AssetBundle Bundle;

            /// <summary>
            /// 引用计数
            /// </summary>
            public int RefCount;
        }
        #endregion

        #region variables | 变量
        /// <summary>
        /// 是否就绪（Resources模式始终为true，AB模式加载了manifest后为true）
        /// </summary>
        public static bool OK;

        /// <summary>
        /// 清单文件
        /// </summary>
        public static AssetBundleManifest Manifest;

        /// <summary>
        /// AB主文件
        /// </summary>
        public static AssetBundle MainBundle;

        /// <summary>
        /// 正在加载的Scene文件
        /// </summary>
        public static Dictionary<string, SceneTask> LoadingScenes = new Dictionary<string, SceneTask>();

        /// <summary>
        /// 正在加载的Asset文件
        /// </summary>
        public static Dictionary<string, AssetTask> LoadingAssets = new Dictionary<string, AssetTask>();

        /// <summary>
        /// 正在加载的AB文件
        /// </summary>
        public static Dictionary<string, BundleTask> LoadingBundles = new Dictionary<string, BundleTask>();

        /// <summary>
        /// 所有加载的AB文件
        /// </summary>
        public static Dictionary<string, BundleInfo> LoadedBundles = new Dictionary<string, BundleInfo>();
        #endregion

        #region initialize | 初始化
        public static event Action<string> BeforeLoadAsset;
        public static event Action<string> AfterLoadAsset;
        public static event Action<string> BeforeLoadScene;
        public static event Action<string> AfterLoadScene;

        /// <summary>
        /// 初始化（加载AB的manifest）
        /// </summary>
        public static void Initialize()
        {
            if (OK == false)
            {
                LoadManifest();
            }
        }

        /// <summary>
        /// 加载AB的manifest
        /// </summary>
        public static void LoadManifest()
        {
            if (Constants.ASSET_BUNDLE_MODE)
            {
                try
                {
                    if (MainBundle != null)
                    {
                        MainBundle.Unload(true);
                    }
                }
                catch { }
                var path = Constants.LOCAL_ASSET_BUNDLE_PATH + Constants.ASSET_BUNDLE_MANIFEST_FILE;
                if (File.Exists(path))
                {
                    try
                    {
                        MainBundle = Helper.LoadAssetBundle(path);
                        Manifest = MainBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                        OK = true;
                    }
                    catch
                    {
                        OK = false;
                        Helper.LogError(Constants.RELEASE_MODE ? null : "loadmanifest error.");
                    }
                }
                else
                {
                    OK = false;
                }
            }
            else
            {
                OK = true;
            }
        }
        #endregion

        #region bundle operation | bundle操作
        /// <summary>
        /// 加载AB
        /// </summary>
        /// <param name="bundleName"></param>
        /// <returns></returns>
        public static AssetBundle LoadAssetBundle(string bundleName)
        {
            if (LoadedBundles.TryGetValue(bundleName, out BundleInfo bundleInfo) == false)
            {
                string[] deps = Manifest.GetAllDependencies(bundleName);
                if (deps != null || deps.Length > 0)
                {
                    for (int i = 0; i < deps.Length; i++)
                    {
                        string dep = deps[i];
                        if (LoadedBundles.TryGetValue(dep, out BundleInfo info) == false)
                        {
                            string path = Constants.LOCAL_ASSET_BUNDLE_PATH + dep;
                            AssetBundle bundle = Helper.LoadAssetBundle(path);
                            info = new BundleInfo() { Bundle = bundle, RefCount = 1 };
                            if (LoadedBundles.ContainsKey(dep))
                            {
                                LoadedBundles.Remove(dep);
                            }
                            LoadedBundles.Add(dep, info);
                        }
                        else
                        {
                            info.RefCount++;
                        }
                    }
                }

                string bundleFilePath = Constants.LOCAL_ASSET_BUNDLE_PATH + bundleName;
                if (File.Exists(bundleFilePath))
                {
                    AssetBundle bundle = Helper.LoadAssetBundle(bundleFilePath);
                    bundleInfo = new BundleInfo()
                    {
                        RefCount = 1,
                        Bundle = bundle
                    };
                    if (LoadedBundles.ContainsKey(bundleName))
                    {
                        LoadedBundles.Remove(bundleName);
                    }
                    LoadedBundles.Add(bundleName, bundleInfo);
                    return bundleInfo.Bundle;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                bundleInfo.RefCount++;
                return bundleInfo.Bundle;
            }
        }

        /// <summary>
        /// 加载AB（异步）
        /// </summary>
        /// <param name="bundleName"></param>
        /// <param name="handler">异步句柄</param>
        /// <returns></returns>
        public static IEnumerator LoadAssetBundleAsync(string bundleName, Handler handler)
        {
            if (LoadedBundles.TryGetValue(bundleName, out BundleInfo bundleInfo1) == false)
            {
                string[] deps = Manifest.GetAllDependencies(bundleName);
                handler.TotalCount += deps.Length + 1; // 依赖 + 自己
                if (deps != null || deps.Length > 0)
                {
                    for (int i = 0; i < deps.Length; i++)
                    {
                        string dep = deps[i];
                        if (LoadedBundles.TryGetValue(dep, out BundleInfo bundleInfo2) == false)
                        {
                            if (LoadingBundles.TryGetValue(dep, out BundleTask task) == false)
                            {
                                var path = Constants.LOCAL_ASSET_BUNDLE_PATH + dep;
                                var req = Helper.LoadAssetBundleAsync(path);
                                task = new BundleTask() { Name = dep, Req = req };
                                LoadingBundles.Add(dep, task);
                                yield return new WaitUntil(() => task.Req.isDone);
                                LoadingBundles.Remove(dep);
                            }
                            else
                            {
                                yield return new WaitUntil(() => task.Req.isDone);
                            }
                            BundleInfo bundle = new BundleInfo() { Bundle = task.Req.assetBundle, RefCount = 1 };
                            if (LoadedBundles.ContainsKey(dep)) LoadedBundles.Remove(dep);
                            LoadedBundles.Add(dep, bundle);
                        }
                        else
                        {
                            bundleInfo2.RefCount++;
                        }
                        handler.DoneCount++;
                    }
                }

                if (LoadedBundles.TryGetValue(bundleName, out BundleInfo bundleInfo3) == false)
                {
                    if (LoadingBundles.TryGetValue(bundleName, out BundleTask task) == false)
                    {
                        var path = Constants.LOCAL_ASSET_BUNDLE_PATH + bundleName;
                        var req = Helper.LoadAssetBundleAsync(path);
                        task = new BundleTask() { Name = bundleName, Req = req };
                        LoadingBundles.Add(bundleName, task);
                        yield return new WaitUntil(() => task.Req.isDone);
                        LoadingBundles.Remove(bundleName);
                    }
                    else
                    {
                        yield return new WaitUntil(() => task.Req.isDone);
                    }
                    bundleInfo3 = new BundleInfo() { Bundle = task.Req.assetBundle, RefCount = 1 };
                    if (LoadedBundles.ContainsKey(bundleName))
                    {
                        LoadedBundles.Remove(bundleName);
                    }
                    LoadedBundles.Add(bundleName, bundleInfo3);
                }
                else
                {
                    bundleInfo3.RefCount++;
                }
                handler.DoneCount++;
            }
            else
            {
                bundleInfo1.RefCount++;
            }
            yield return 0;
        }

        /// <summary>
        /// 卸载AB
        /// </summary>
        /// <param name="bundleName"></param>
        public static void UnloadAssetBundle(string bundleName)
        {
            if (LoadedBundles.TryGetValue(bundleName, out BundleInfo bundleInfo))
            {
                string[] deps = Manifest.GetAllDependencies(bundleName);
                if (deps != null || deps.Length > 0)
                {
                    for (int i = 0; i < deps.Length; i++)
                    {
                        string depName = deps[i];
                        if (LoadedBundles.TryGetValue(depName, out BundleInfo dbundle))
                        {
                            dbundle.RefCount--;
                            if (dbundle.Bundle == null)
                            {
                                LoadedBundles.Remove(depName);
                            }
                            else if (dbundle.RefCount <= 0)
                            {
                                dbundle.Bundle.Unload(true);
                                LoadedBundles.Remove(depName);
                            }
                        }
                    }
                }

                bundleInfo.RefCount--;
                if (bundleInfo.Bundle == null)
                {
                    LoadedBundles.Remove(bundleName);
                }
                else if (bundleInfo.RefCount <= 0)
                {
                    bundleInfo.Bundle.Unload(true);
                    LoadedBundles.Remove(bundleName);
                }
            }
        }
        #endregion

        #region asset operation | 资源操作
#if UNITY_EDITOR
        public static string[] editorAssets = UnityEditor.AssetDatabase.GetAllAssetPaths();
#endif

        /// <summary>
        /// 加载资源
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <param name="type">资源类型</param>
        /// <param name="_internal">resources内部资源</param>
        /// <returns></returns>
        public static UnityEngine.Object LoadAsset(string assetPath, Type type, bool _internal = false)
        {
            BeforeLoadAsset?.Invoke(assetPath);
            UnityEngine.Object asset = null;
            try
            {
                if (_internal)
                {
                    if (assetPath.StartsWith("Resources/")) assetPath = assetPath.Replace("Resources/", "");
                    asset = Resources.Load(assetPath, type);
                }
                else if (!Constants.ASSET_BUNDLE_MODE)
                {
                    if (assetPath.StartsWith("RawAssets"))
                    {
#if UNITY_EDITOR
                        List<string> paths = editorAssets.Where((e) => e.Contains(assetPath)).ToList();
                        for (int i = 0; i < paths.Count; i++)
                        {
                            var path = paths[i];
                            asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, type);
                            if (asset) break;
                        }
#else
                        Helper.LogError("can not load asset at path {0}",assetPath);
#endif
                    }
                    else if (assetPath.StartsWith("Resources/"))
                    {
                        assetPath = assetPath.Replace("Resources/", "");
                        asset = Resources.Load(assetPath, type);
                    }
                    else
                    {
                        Helper.LogError("can not load asset at path {0}", assetPath);
                    }
                }
                else
                {
                    int index = assetPath.LastIndexOf("/");
                    string assetName = assetPath.Substring(index + 1);
                    string bundleName = assetPath.Substring(0, index);
                    bundleName = bundleName.Replace("/", "_");
                    bundleName = bundleName.ToLower();
                    bundleName = bundleName + Constants.ASSET_BUNDLE_FILE_EXTENSION;
                    AssetBundle bundle = LoadAssetBundle(bundleName);
                    if (bundle)
                    {
                        asset = bundle.LoadAsset(assetName, type);
                    }
                }
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                AfterLoadAsset?.Invoke(assetPath);
            }
            return asset;
        }

        /// <summary>
        /// 执行异步加载
        /// </summary>
        /// <param name="assetPath"></param>
        /// <param name="type"></param>
        /// <param name="cb"></param>
        /// <param name="handler"></param>
        /// <param name="_internal"></param>
        /// <returns></returns>
        private static IEnumerator DoLoadAsset(string assetPath, Type type, Callback cb, Handler handler, bool _internal = false)
        {
            BeforeLoadAsset?.Invoke(assetPath);
            UnityEngine.Object asset = null;
            if (_internal)
            {
                if (assetPath.StartsWith("Resources/")) assetPath = assetPath.Replace("Resources/", "");
                if (LoadingAssets.TryGetValue(assetPath, out AssetTask task) == false)
                {
                    ResourceRequest req = Resources.LoadAsync(assetPath, type);
                    task = new AssetTask() { Name = assetPath, Req = req };
                    LoadingAssets.Add(assetPath, task);
                    yield return new WaitUntil(() => task.Req.isDone);
                    LoadingAssets.Remove(assetPath);
                }
                else
                {
                    yield return new WaitUntil(() => task.Req.isDone);
                }
                asset = (task.Req as ResourceRequest).asset;
            }
            else if (!Constants.ASSET_BUNDLE_MODE)
            {
                if (assetPath.StartsWith("RawAssets"))
                {
#if UNITY_EDITOR
                    List<string> paths = editorAssets.Where((e) => e.Contains(assetPath)).ToList();
                    for (int i = 0; i < paths.Count; i++)
                    {
                        var path = paths[i];
                        asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, type);
                        if (asset) break;
                    }
#else
                    Helper.LogError("can not load asset at path {0}",assetPath);
#endif
                }
                else if (assetPath.StartsWith("Resources/"))
                {
                    assetPath = assetPath.Replace("Resources/", "");
                    if (LoadingAssets.TryGetValue(assetPath, out AssetTask task) == false)
                    {
                        ResourceRequest req = Resources.LoadAsync(assetPath, type);
                        task = new AssetTask() { Name = assetPath, Req = req };
                        LoadingAssets.Add(assetPath, task);
                        yield return new WaitUntil(() => task.Req.isDone);
                        LoadingAssets.Remove(assetPath);
                    }
                    else
                    {
                        yield return new WaitUntil(() => task.Req.isDone);
                    }
                    asset = (task.Req as ResourceRequest).asset;
                }
                else
                {
                    Helper.LogError("can not load asset at path {0}", assetPath);
                }
            }
            else
            {
                handler.TotalCount++;// Load任务
                int index = assetPath.LastIndexOf("/");
                string assetName = assetPath.Substring(index + 1);
                string bundleName = assetPath.Substring(0, index);
                bundleName = bundleName.Replace("/", "_");
                bundleName = bundleName.ToLower();
                bundleName += Constants.ASSET_BUNDLE_FILE_EXTENSION;
                yield return Loom.StartCR(LoadAssetBundleAsync(bundleName, handler));
                if (LoadedBundles.TryGetValue(bundleName, out BundleInfo bundleInfo))
                {
                    if (LoadingAssets.TryGetValue(assetPath, out AssetTask task) == false)
                    {
                        AssetBundleRequest req = bundleInfo.Bundle.LoadAssetAsync(assetName, type);
                        task = new AssetTask() { Name = assetPath, Req = req };
                        handler.Operation = req;
                        handler.DoWillLoad();
                        LoadingAssets.Add(assetPath, task);
                        yield return req;
                        asset = req.asset;
                        LoadingAssets.Remove(assetPath);
                        handler.DoneCount++;
                        handler.DoAfterLoad();
                    }
                    else
                    {
                        handler.Operation = task.Req;
                        handler.DoWillLoad();
                        yield return new WaitUntil(() => task.Req.isDone);
                        handler.DoneCount++;
                        handler.DoAfterLoad();
                    }
                    asset = (task.Req as AssetBundleRequest).asset;
                }
                else
                {
                    Helper.LogError("load {0} error", bundleName);
                }
            }
            AfterLoadAsset?.Invoke(assetPath);
            cb?.Invoke(asset);
            yield return 0;
        }

        /// <summary>
        /// 加载资源（异步）
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <param name="type">资源类型</param>
        /// <param name="_internal">resources内部资源</param>
        /// <returns></returns>
        public static Handler LoadAssetAsync(string assetPath, Type type, Callback cb, bool _internal = false)
        {
            Handler handler = new Handler();
            Loom.StartCR(DoLoadAsset(assetPath, type, cb, handler, _internal));
            return handler;
        }

        /// <summary>
        /// 卸载资源
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        public static void UnloadAsset(string assetPath)
        {
            if (Constants.ASSET_BUNDLE_MODE)
            {
                int index = assetPath.LastIndexOf("/");
                string bundleName = assetPath.Substring(0, index);
                bundleName = bundleName.Replace("/", "_");
                bundleName = bundleName.ToLower();
                bundleName = bundleName + Constants.ASSET_BUNDLE_FILE_EXTENSION;
                UnloadAssetBundle(bundleName);
            }
        }
        #endregion

        #region scene operation | 场景操作
        /// <summary>
        /// 加载场景
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns></returns>
        public static void LoadScene(string sceneName)
        {
            BeforeLoadScene?.Invoke(sceneName);
            try
            {
                if (Constants.ASSET_BUNDLE_MODE)
                {
                    string bundleName = Helper.StringFormat("rawassets_bundle_scenes_{0}.asset", sceneName.ToLower());
                    AssetBundle bundle = LoadAssetBundle(bundleName);
                    if (bundle == null)
                    {
                        Helper.LogError(Constants.RELEASE_MODE ? null : "can not load scene caused by nil scene bundle file.");
                    }
                    else
                    {
                        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
                    }
                }
                else
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
                }
            }
            catch (Exception e)
            {
                throw e;
            }
            finally
            {
                AfterLoadScene?.Invoke(sceneName);
            }
        }

        /// <summary>
        /// 执行异步加载
        /// </summary>
        /// <param name="sceneName"></param>
        /// <param name="handler">异步句柄</param>
        /// <returns></returns>
        private static IEnumerator DoLoadScene(string sceneName, Handler handler)
        {
            BeforeLoadScene?.Invoke(sceneName);
            if (Constants.ASSET_BUNDLE_MODE)
            {
                handler.TotalCount++;// Load任务
                string bundleName = Helper.StringFormat("rawassets_bundle_scenes_{0}.asset", sceneName.ToLower());
                yield return Loom.StartCR(LoadAssetBundleAsync(bundleName, handler));
                if (LoadedBundles.TryGetValue(bundleName, out _))
                {
                    if (LoadingScenes.TryGetValue(sceneName, out SceneTask task) == false)
                    {
                        var req = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
                        task = new SceneTask() { Name = sceneName, Req = req };
                        handler.Operation = req;
                        handler.DoWillLoad();
                        LoadingScenes.Add(sceneName, task);
                        yield return new WaitUntil(() => task.Req.isDone);
                        LoadingScenes.Remove(sceneName);
                        handler.DoneCount++;
                        handler.DoAfterLoad();
                    }
                    else
                    {
                        handler.Operation = task.Req;
                        handler.DoWillLoad();
                        yield return new WaitUntil(() => task.Req.isDone);
                        handler.DoneCount++;
                        handler.DoAfterLoad();
                    }
                }
                else
                {
                    Helper.LogError("load {0} error", sceneName);
                }
            }
            else
            {
                if (LoadingScenes.TryGetValue(sceneName, out SceneTask task) == false)
                {
                    var req = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName);
                    task = new SceneTask() { Name = sceneName, Req = req };
                    handler.Operation = req;
                    handler.DoWillLoad();
                    LoadingScenes.Add(sceneName, task);
                    yield return new WaitUntil(() => task.Req.isDone);
                    LoadingScenes.Remove(sceneName);
                    handler.DoAfterLoad();
                }
                else
                {
                    handler.Operation = task.Req;
                    handler.DoWillLoad();
                    yield return new WaitUntil(() => task.Req.isDone);
                    handler.DoAfterLoad();
                }
            }
            AfterLoadScene?.Invoke(sceneName);
            yield return 0;
        }

        /// <summary>
        /// 加载场景（异步）
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <returns></returns>
        public static Handler LoadSceneAsync(string sceneName)
        {
            Handler handler = new Handler();
            Loom.StartCR(DoLoadScene(sceneName, handler));
            return handler;
        }

        /// <summary>
        /// 卸载场景
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        public static void UnloadScene(string sceneName)
        {
            if (Constants.ASSET_BUNDLE_MODE)
            {
                UnloadAssetBundle(Helper.StringFormat("rawassets_bundle_scenes_{0}.asset", sceneName.ToLower()));
            }
        }
        #endregion

        #region for updater | updater相关
        /// <summary>
        /// 加载所有的AB
        /// </summary>
        public static void LoadAll()
        {
            if (Constants.ASSET_BUNDLE_MODE)
            {
                float stime = Time.realtimeSinceStartup;
                int count = 0;
                string manifest = Helper.StringFormat("{0}{1}", Constants.LOCAL_ASSET_BUNDLE_PATH, Constants.MANIFEST_FILE);
                if (File.Exists(manifest) == false)
                {
                    return;
                }
                string[] lines = File.ReadAllLines(manifest);
                if (lines == null || lines.Length == 0)
                {
                    return;
                }
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string bundleName = line.Split('|')[0];
                    if (bundleName.EndsWith(Constants.ASSET_BUNDLE_FILE_EXTENSION))
                    {
                        if (LoadedBundles.TryGetValue(bundleName, out _) == false)
                        {
                            string bundleFilePath = Constants.LOCAL_ASSET_BUNDLE_PATH + bundleName;
                            if (File.Exists(bundleFilePath))
                            {
                                float ttime = Time.realtimeSinceStartup;
                                AssetBundle bundle = Helper.LoadAssetBundle(bundleFilePath);
                                Helper.Log(Constants.RELEASE_MODE ? null : "load {0} cost {1}s", bundleName, Time.realtimeSinceStartup - ttime);
                                count++;

                                BundleInfo bundleInfo = new BundleInfo()
                                {
                                    RefCount = 1,
                                    Bundle = bundle
                                };
                                if (LoadedBundles.ContainsKey(bundleName))
                                {
                                    LoadedBundles.Remove(bundleName);
                                }
                                LoadedBundles.Add(bundleName, bundleInfo);
                            }
                        }
                    }
                }
                Helper.Log(Constants.RELEASE_MODE ? null : "load {0} bundle(s) cost {1}s", count, Time.realtimeSinceStartup - stime);
            }
        }

        /// <summary>
        /// 卸载所有的AB（强制卸载）
        /// </summary>
        public static void UnloadAll()
        {
            foreach (var item in LoadedBundles)
            {
                item.Value.Bundle.Unload(true);
            }
            LoadedBundles.Clear();
        }

        /// <summary>
        /// 加载差异的AB
        /// </summary>
        /// <param name="differ"></param>
        public static void LoadDiff(FileManifest.DifferInfo differ)
        {
            if (Constants.ASSET_BUNDLE_MODE && differ != null)
            {
                if (differ.Deleted.Count > 0)
                {
                    for (int i = 0; i < differ.Deleted.Count; i++)
                    {
                        FileManifest.FileInfo info = differ.Deleted[i];
                        string bundleName = info.Name;
                        if (bundleName.EndsWith(Constants.ASSET_BUNDLE_FILE_EXTENSION))
                        {
                            UnloadDiff(bundleName);
                        }
                    }
                }
                if (differ.Modified.Count > 0)
                {
                    for (int i = 0; i < differ.Modified.Count; i++)
                    {
                        FileManifest.FileInfo info = differ.Modified[i];
                        string bundleName = info.Name;
                        if (bundleName.EndsWith(Constants.ASSET_BUNDLE_FILE_EXTENSION))
                        {
                            UnloadDiff(bundleName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 卸载差异的AB
        /// </summary>
        /// <param name="bundleName"></param>
        private static void UnloadDiff(string bundleName)
        {
            if (LoadedBundles.ContainsKey(bundleName))
            {
                List<string> krs = new List<string>();
                krs.Add(bundleName);
                foreach (var kvp in LoadedBundles)
                {
                    string[] deps = Manifest.GetAllDependencies(kvp.Key);
                    if (deps != null && deps.Length > 0 && kvp.Key != bundleName)
                    {
                        for (int j = 0; j < deps.Length; j++)
                        {
                            string dep = deps[j];
                            if (dep == bundleName)
                            {
                                krs.Add(kvp.Key);
                                break;
                            }
                        }
                    }
                }
                for (int j = 0; j < krs.Count; j++)
                {
                    string key = krs[j];
                    if (LoadedBundles.TryGetValue(key, out BundleInfo bundleInfo))
                    {
                        if (bundleInfo.Bundle)
                        {
                            bundleInfo.Bundle.Unload(true);
                        }
                        LoadedBundles.Remove(key);
                        UnloadDiff(key);
                    }
                }
            }
        }
        #endregion

        #region other api | 其他接口
        /// <summary>
        /// 获取当前加载的进度（0-1）
        /// </summary>
        /// <returns></returns>
        public static float Progress()
        {
            float total = LoadingScenes.Count + LoadingAssets.Count + LoadingBundles.Count;
            if (total == 0f) return 1;
            float current = 0f;
            if (LoadingScenes.Count > 0)
            {
                foreach (var item in LoadingScenes)
                {
                    current += item.Value.Req.progress;
                }
            }
            if (LoadingAssets.Count > 0)
            {
                foreach (var item in LoadingAssets)
                {
                    current += item.Value.Req.progress;
                }
            }
            if (LoadingBundles.Count > 0)
            {
                foreach (var item in LoadingBundles)
                {
                    current += item.Value.Req.progress;
                }
            }
            return current / total;
        }

        /// <summary>
        /// 是否正在加载资源/场景
        /// </summary>
        /// <returns></returns>
        public static bool Busy()
        {
            return LoadingAssets.Count > 0 || LoadingBundles.Count > 0 || LoadingScenes.Count > 0;
        }
        #endregion
    }
}