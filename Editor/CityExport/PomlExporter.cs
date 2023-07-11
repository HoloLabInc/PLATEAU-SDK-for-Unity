using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using GLTFast.Export;
using PLATEAU.CityInfo;
using PLATEAU.Native;
using UnityEngine;

namespace PLATEAU.Editor.CityExport
{
    /// <summary>
    /// UnityのモデルをPOMLファイルに出力します。
    /// </summary>
    internal static class PomlExporter
    {
        private static bool exporting = false;

        public static void Export(string destDir, PLATEAUInstancedCityModel instancedCityModel, MeshExportOptions options)
        {
            if (instancedCityModel == null)
            {
                Debug.LogError($"{nameof(instancedCityModel)} is null.");
                return;
            }
            destDir = destDir.Replace('\\', '/');
            if (!Directory.Exists(destDir))
            {
                Debug.LogError($"Destination Path is not a folder. destination = '{destDir}'");
                return;
            }

            ExportModel(destDir, instancedCityModel, options);
        }

        private static bool ExportModel(string destDir, PLATEAUInstancedCityModel instancedCityModel, MeshExportOptions options)
        {
            if (exporting)
            {
                Debug.LogWarning("エクスポート実行中です");
                return false;
            }

            exporting = true;
            try
            {
                // Unityのシーンから情報を読みます。
                var trans = instancedCityModel.transform;
                int numChild = trans.childCount;
                for (int i = 0; i < numChild; i++)
                {
                    var childTrans = trans.GetChild(i);
                    var childName = childTrans.name;
                    if (!childName.EndsWith(".gml")) continue;

                    if ((!options.ExportHiddenObjects) && (!childTrans.gameObject.activeInHierarchy))
                    {
                        continue;
                    }

                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(childName);
                    Debug.Log($"Export started: {fileNameWithoutExtension}");
                    var result = ExportPomlZip(destDir, fileNameWithoutExtension, instancedCityModel, childTrans.gameObject);
                    if (result == false)
                    {
                        return false;
                    }

                    Debug.Log($"Export finished: {fileNameWithoutExtension}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
            finally
            {
                exporting = false;
            }
        }

        private static bool ExportGlb(string exportPath, GameObject targetGameObject)
        {
            var exportSettings = new ExportSettings
            {
                Format = GltfFormat.Binary,
                FileConflictResolution = FileConflictResolution.Overwrite,
            };

            var export = new GameObjectExport(exportSettings);

            // Add a scene
            export.AddScene(new[] { targetGameObject });

            try
            {
                var success = AsyncHelpers.RunSync(() => export.SaveToFileAndDispose(exportPath));

                if (!success)
                {
                    Debug.LogError("Something went wrong exporting a glTF");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private static bool ExportPomlZip(string destDir, string fileNameWithoutExtension, PLATEAUInstancedCityModel plateauInstancedCityModel, GameObject targetGameObject)
        {
            string dirPath = Path.Combine(destDir, $"Temp_{fileNameWithoutExtension}");
            Directory.CreateDirectory(dirPath);

            string fileExtension = ".glb";
            string gltfFilePath = Path.Combine(dirPath, fileNameWithoutExtension + fileExtension);

            var glbResult = ExportGlb(gltfFilePath, targetGameObject);
            if (glbResult == false)
            {
                return false;
            }

            // POMLファイルを出力します。
            using var geoRef = plateauInstancedCityModel.GeoReference;
            var geoCoord = geoRef.Unproject(new PlateauVector3d(0, 0, 0));

            var poml = $@"<poml>
  <scene>
    <model src=""./{fileNameWithoutExtension}{fileExtension}"">
      <geo-reference latitude=""{geoCoord.Latitude}"" longitude=""{geoCoord.Longitude}"" ellipsoidal-height=""{geoCoord.Height}"">
      </geo-reference>
    </model>
  </scene>
</poml>";
            string pomlFilePath = Path.Combine(dirPath, fileNameWithoutExtension + ".poml");
            File.WriteAllText(pomlFilePath, poml);

            var pomlZipFilePath = Path.Combine(destDir, fileNameWithoutExtension + ".poml.zip");
            CreateZip(new List<string>() { pomlFilePath, gltfFilePath }, pomlZipFilePath);

            try
            {
                File.Delete(gltfFilePath);
                File.Delete(pomlFilePath);
                Directory.Delete(dirPath, false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            return true;
        }

        private static void CreateZip(List<string> filePathList, string zipFilePath)
        {
            using (var zipFileStream = new FileStream(zipFilePath, FileMode.Create))
            {
                using (var zipArchive = new ZipArchive(zipFileStream, ZipArchiveMode.Create))
                {
                    foreach (string filePath in filePathList)
                    {
                        // Add the file to the zip archive.
                        string fileNameInZip = Path.GetFileName(filePath);
                        ZipArchiveEntry zipEntry = zipArchive.CreateEntry(fileNameInZip);

                        // Copy the file content to the zip entry.
                        using (FileStream sourceFileStream = new FileStream(filePath, FileMode.Open))
                        {
                            using (Stream zipEntryStream = zipEntry.Open())
                            {
                                sourceFileStream.CopyTo(zipEntryStream);
                            }
                        }
                    }
                }
            }
        }
    }
}
