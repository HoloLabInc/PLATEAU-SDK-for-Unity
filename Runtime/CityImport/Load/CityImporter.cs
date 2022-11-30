using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PLATEAU.CityAdjust;
using PLATEAU.CityGML;
using PLATEAU.CityImport.Load.Convert;
using PLATEAU.CityImport.Load.FileCopy;
using PLATEAU.CityImport.Setting;
using PLATEAU.CityInfo;
using PLATEAU.Geometries;
using PLATEAU.Interop;
using PLATEAU.Dataset;
using PLATEAU.Util;
using UnityEngine;

namespace PLATEAU.CityImport.Load
{
    /// <summary>
    /// GMLファイルに記載された都市モデルを Unity にインポートします。
    /// </summary>
    internal static class CityImporter
    {
        // インポート設定のうち、Unityで共通するものです。
        private const CoordinateSystem meshAxes = CoordinateSystem.EUN;
        private const float unitScale = 1.0f;

        /// <summary>
        /// <see cref="CityImporter"/> クラスのメインメソッドです。
        /// GMLファイルから都市モデルを読み、そのメッシュをUnity向けに変換してシーンに配置します。
        /// メインスレッドで呼ぶ必要があります。
        /// </summary>
        public static async Task ImportAsync(CityLoadConfig config, IProgressDisplay progressDisplay)
        {
            // string sourcePath = config.SourcePathBeforeImport;
            var datasetSourceConfig = config.DatasetSourceConfig;
            string destPath = PathUtil.PLATEAUSrcFetchDir;
            if (config.DatasetSourceConfig.IsServer)
            {
                destPath = Path.Combine(destPath, config.DatasetSourceConfig.RootDirName);
            }
            string destFolderName = datasetSourceConfig.RootDirName;

            if ((!datasetSourceConfig.IsServer) && (!Directory.Exists(datasetSourceConfig.DatasetIdOrSourcePath)))
            {
                Debug.LogError($"インポート元パスが存在しません。 sourcePath = {datasetSourceConfig.DatasetIdOrSourcePath}");
                return;
            }
            
            progressDisplay.SetProgress("GMLファイル検索", 10f, "");
            using var datasetSource = DatasetSource.Create(datasetSourceConfig);
            var datasetAccessor = datasetSource.Accessor;
            var targetGmls = await Task.Run(() => CityFilesCopy.FindTargetGmls(
                datasetAccessor, config
            ));
            progressDisplay.SetProgress("GMLファイル検索", 100f, "完了");

            if (targetGmls.Count <= 0)
            {
                Debug.LogError("該当するGMLファイルがありません。");
                return;
            }

            foreach (var gml in targetGmls)
            {
                progressDisplay.SetProgress(Path.GetFileName(gml.Path), 0f, "未処理");
            }

            var rootTrans = new GameObject(destFolderName).transform;

            // 各GMLファイルで共通する設定です。
            var referencePoint = CalcCenterPoint(targetGmls, config.CoordinateZoneID);
            
            // ルートのGameObjectにコンポーネントを付けます。 
            var cityModelComponent = rootTrans.gameObject.AddComponent<PLATEAUInstancedCityModel>();
            cityModelComponent.GeoReference =
                new GeoReference(referencePoint, unitScale, meshAxes, config.CoordinateZoneID);
            
            // GMLファイルを同時に処理する最大数です。
            // 並列数が 4 くらいだと、1つずつ処理するよりも、全部同時に処理するよりも速いという経験則です。
            var sem = new SemaphoreSlim(4);
            
            await Task.WhenAll(targetGmls.Select(async gmlInfo =>
            {
                await sem.WaitAsync(); 
                try
                {
                    // ここはメインスレッドで呼ぶ必要があります。
                    await ImportGml(gmlInfo, destPath, config, rootTrans, progressDisplay, referencePoint);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
                finally
                {
                    sem.Release();
                }

            }));
            
            // インポート完了後の処理
            CityDuplicateProcessor.EnableOnlyLargestLODInDuplicate(cityModelComponent);
            // foreach (var gmlInfo in targetGmls) gmlInfo.Dispose();
        }

        /// <summary>
        /// GMLファイルを1つインポートします。
        /// メインスレッドで呼ぶ必要があります。
        /// </summary>
        private static async Task ImportGml(GmlFile gmlInfo, string destPath, CityLoadConfig conf,
            Transform rootTrans, IProgressDisplay progressDisplay,
            PlateauVector3d referencePoint)
        {
            if (gmlInfo.Path == null)
            {
                return;
            }
            
            string gmlName = Path.GetFileName(gmlInfo.Path);
            // TODO サーバーのときは「ダウンロード中」という表示にする
            progressDisplay.SetProgress(gmlName, 0f, "インポート処理中");

            destPath = destPath.Replace('\\', '/');
            if (!destPath.EndsWith("/")) destPath += "/";
            
            // GMLと関連ファイルを StreamingAssets にコピーします。
            // ここは別スレッドで実行可能です。
            var fetchedGmlInfo = await Task.Run(() => gmlInfo.Fetch(destPath));
            // ここでメインスレッドに戻ります。
            progressDisplay.SetProgress(gmlName, 20f, "GMLファイルをロード中");
            string gmlPathAfter = fetchedGmlInfo.Path;

            using var cityModel = await LoadGmlAsync(fetchedGmlInfo);
            if (cityModel == null)
            {
                progressDisplay.SetProgress(gmlName, 0f, "失敗 : GMLファイルのパースに失敗しました。");
                return;
            }

            string udxFeature = $"/udx/{fetchedGmlInfo.FeatureType}/";
            string relativeGmlPathFromFeature =
                gmlPathAfter.Substring(
                    gmlPathAfter.LastIndexOf(udxFeature,
                        StringComparison.Ordinal) + udxFeature.Length);
            // gmlファイルに対応するゲームオブジェクトの名称は、地物タイプフォルダからの相対パスにします。
            string gmlObjName = relativeGmlPathFromFeature;
            var gmlTrans = new GameObject(gmlObjName).transform;
            
            gmlTrans.parent = rootTrans;
            var package = fetchedGmlInfo.Package;
            var packageConf = conf.GetConfigForPackage(package);
            var meshExtractOptions = MeshExtractOptions.DefaultValue();
            meshExtractOptions.ReferencePoint = referencePoint;
            meshExtractOptions.MeshAxes = meshAxes;
            meshExtractOptions.MeshGranularity = packageConf.meshGranularity;
            meshExtractOptions.MaxLOD = packageConf.maxLOD;
            meshExtractOptions.MinLOD = packageConf.minLOD;
            meshExtractOptions.ExportAppearance = packageConf.includeTexture;
            meshExtractOptions.GridCountOfSide = 10; // TODO gridCountOfSideはユーザーが設定できるようにしたほうが良い
            meshExtractOptions.UnitScale = unitScale;
            meshExtractOptions.CoordinateZoneID = conf.CoordinateZoneID;
            meshExtractOptions.ExcludeCityObjectOutsideExtent = ShouldExcludeCityObjectOutsideExtent(package);
            meshExtractOptions.ExcludeTrianglesOutsideExtent = ShouldExcludeTrianglesOutsideExtent(package);
            meshExtractOptions.Extent = conf.Extent;

            if (!meshExtractOptions.Validate(out var failureMessage))
            {
                progressDisplay.SetProgress(gmlName, 0f, $"失敗 : メッシュ設定に不正な点があります。 : {failureMessage}");
                return;
            }

            // ここはメインスレッドで呼ぶ必要があります。
            bool placingSucceed = await PlateauToUnityModelConverter.ConvertAndPlaceToScene(
                cityModel, meshExtractOptions, gmlTrans, progressDisplay, gmlName, packageConf.doSetMeshCollider
            );
            if (placingSucceed)
            {
                progressDisplay.SetProgress(gmlName, 100f, "完了");
            }
            else
            {
                progressDisplay.SetProgress(gmlName, 0f, "失敗 : モデルの変換または配置に失敗しました。");
            }
            
        }

        private static bool ShouldExcludeCityObjectOutsideExtent(PredefinedCityModelPackage package)
        {
            if (package == PredefinedCityModelPackage.Relief) return false;
            return true;
        }

        private static bool ShouldExcludeTrianglesOutsideExtent(PredefinedCityModelPackage package)
        {
            return !ShouldExcludeCityObjectOutsideExtent(package);
        }
        
        

        private static PlateauVector3d CalcCenterPoint(IEnumerable<GmlFile> targetGmls, int coordinateZoneID)
        {
            using var geoReference = CoordinatesConvertUtil.UnityStandardGeoReference(coordinateZoneID);
            var geoCoordSum = new GeoCoordinate(0, 0, 0);
            int count = 0;
            foreach (var gml in targetGmls)
            {
                geoCoordSum += gml.MeshCode.Extent.Center;
                count++;
            }

            if (count == 0) throw new ArgumentException("Target gmls count is zero.");
            var centerGeo = geoCoordSum / count;
            return geoReference.Project(centerGeo);
        }

        private static async Task<CityModel> LoadGmlAsync(GmlFile gmlInfo)
        {
            string gmlPath = gmlInfo.Path;

            // GMLをパースした結果を返しますが、失敗した時は null を返します。
            var cityModel = await Task.Run(() => ParseGML(gmlPath));

            return cityModel;

        }
        
        /// <summary> gmlファイルをパースします。 </summary>
        /// <param name="gmlAbsolutePath"> gmlファイルのパスです。 </param>
        /// <returns><see cref="CityGML.CityModel"/> を返します。ロードに問題があった場合は null を返します。</returns>
        private static CityModel ParseGML(string gmlAbsolutePath)
        {
            if (!File.Exists(gmlAbsolutePath))
            {
                Debug.LogError($"GMLファイルが存在しません。 : {gmlAbsolutePath}");
                return null;
            }
            var parserParams = new CitygmlParserParams(true, true, false);
            
            CityModel cityModel = null;
            try
            {
                cityModel = CityGml.Load(gmlAbsolutePath, parserParams, DllLogCallback.UnityLogCallbacks);
            }
            catch (Exception e)
            {
                Debug.LogError($"GMLファイルのロードに失敗しました。 : {gmlAbsolutePath}.\n{e.Message}");
            }

            return cityModel;
        }
    }
}
