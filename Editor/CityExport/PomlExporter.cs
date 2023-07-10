using System;
using System.IO;
using PLATEAU.CityInfo;
using PLATEAU.Editor.CityExport.ModelConvert;
using PLATEAU.MeshWriter;
using PLATEAU.Native;
using PLATEAU.PolygonMesh;
using UnityEngine;

namespace PLATEAU.Editor.CityExport
{
    /// <summary>
    /// UnityのモデルをPOMLファイルに出力します。
    /// </summary>
    internal static class PomlExporter
    {
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

                using var geoReference = instancedCityModel.GeoReference;
                var referencePoint = geoReference.ReferencePoint;
                var rootPos = trans.position;

                UnityMeshToDllModelConverter.VertexConvertFunc vertexConvertFunc = options.TransformType switch
                {
                    MeshExportOptions.MeshTransformType.Local => src =>
                    {
                        // instancedCityModel を基準とする座標にします。
                        var pos = src - rootPos;
                        return new PlateauVector3d(pos.x, pos.y, pos.z);
                    }
                    ,
                    MeshExportOptions.MeshTransformType.PlaneCartesian => src =>
                    {
                        // 変換時の referencePoint をオフセットします。
                        var pos = referencePoint + new PlateauVector3d(src.x - rootPos.x, src.y - rootPos.y, src.z - rootPos.z);
                        return pos;
                    }
                    ,
                    _ => throw new Exception("Unknown transform type.")
                };

                // Unity のメッシュを中間データ構造(Model)に変換します。
                using var model = UnityMeshToDllModelConverter.Convert(childTrans.gameObject, options.ExportTextures, options.ExportHiddenObjects, false, options.MeshAxis, vertexConvertFunc);

                // Model をファイルにして出力します。
                // options.PlateauModelExporter は、ファイルフォーマットに応じて FbxModelExporter, GltfModelExporter, ObjModelExporter のいずれかです。
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(childName);
                // options.PlateauModelExporter.Export(destDir, fileNameWithoutExtension, model);
                ExportPomlZip(destDir, fileNameWithoutExtension, model, instancedCityModel);
            }
        }

        private static void ExportPomlZip(string destDir, string fileNameWithoutExtension, Model model, PLATEAUInstancedCityModel plateauInstancedCityModel)
        {
            string fileExtension = ".glb";
            string dirPath = Path.Combine(destDir, fileNameWithoutExtension);
            Directory.CreateDirectory(dirPath);

            using (var gltfWriter = new GltfWriter())
            {
                string gltfFilePath = Path.Combine(dirPath, fileNameWithoutExtension + fileExtension);
                string textureDir = Path.Combine(dirPath, "textures");

                gltfWriter.Write(gltfFilePath, model, new GltfWriteOptions(GltfFileFormat.GLB, textureDir));

                Debug.Log(plateauInstancedCityModel.GeoReference.CoordinateSystem);
            }

            // POMLファイルを出力します。
            using var geoRef = plateauInstancedCityModel.GeoReference;
            var geoCoord = geoRef.Unproject(new PlateauVector3d(0, 0, 0));

            var poml = $@"<poml>
    <scene>
        <model src=""./{fileNameWithoutExtension}{fileExtension}"">
            <geo-reference latitude=""{geoCoord.Latitude}"" longitude=""{geoCoord.Longitude} ellipsoidal-height=""{geoCoord.Height}"">
            </geo-reference>
        </model>
    </scene>
</poml>
";
            string pomlFilePath = Path.Combine(dirPath, fileNameWithoutExtension + ".poml");
            File.WriteAllText(pomlFilePath, poml);
        }
    }
}
