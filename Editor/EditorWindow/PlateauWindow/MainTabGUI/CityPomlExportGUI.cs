using System;
using System.Collections.Generic;
using System.Linq;
using PLATEAU.CityInfo;
using PLATEAU.Editor.CityExport;
using PLATEAU.Editor.EditorWindow.Common;
using PLATEAU.Editor.EditorWindow.Common.PathSelector;
using PLATEAU.Editor.EditorWindow.PlateauWindow.MainTabGUI.ExportGUIParts;
using PLATEAU.Geometries;
using UnityEditor;
using UnityEngine;
using Directory = System.IO.Directory;

namespace PLATEAU.Editor.EditorWindow.PlateauWindow.MainTabGUI
{
    /// <summary>
    /// 都市のモデルデータのPOMLエクスポートのGUIです。
    /// </summary>
    internal class CityPomlExportGUI : IEditorDrawable
    {
        private PLATEAUInstancedCityModel exportTarget;

        // private MeshFileFormat meshFileFormat = MeshFileFormat.POML;
        // private IPlateauModelExporter exporter = new PomlModelExporter();
        /*
        private MeshFileFormat meshFileFormat = MeshFileFormat.OBJ;

        private Dictionary<MeshFileFormat, IPlateauModelExporter> formatToExporter = new()
        {
            { MeshFileFormat.OBJ, new ObjModelExporter() },
            { MeshFileFormat.FBX, new FbxModelExporter() },
            { MeshFileFormat.GLTF, new GltfModelExporter() },
            { MeshFileFormat.POML, new PomlModelExporter() }
        };
        */

        private bool exportTextures = true;
        private bool exportHiddenObject;
        private readonly MeshExportOptions.MeshTransformType meshTransformType = MeshExportOptions.MeshTransformType.Local;
        private readonly CoordinateSystem meshAxis = CoordinateSystem.WUN;

        // private static readonly List<CoordinateSystem> meshAxisChoices = ((CoordinateSystem[])Enum.GetValues(typeof(CoordinateSystem))).ToList();
        // private static readonly string[] meshAxisDisplay = meshAxisChoices.Select(axis => axis.ToNaturalLanguage()).ToArray();

        private string exportDirPath = "";
        private bool foldOutOption = true;
        private bool foldOutExportPath = true;
        private PathSelectorFolder exportDirSelector = new PathSelectorFolder();

        public void Draw()
        {
            PlateauEditorStyle.SubTitle("モデルデータのPOMLエクスポートを行います。");
            PlateauEditorStyle.Heading("選択オブジェクト", "num1.png");
            using (PlateauEditorStyle.VerticalScopeLevel1())
            {
                this.exportTarget =
                    (PLATEAUInstancedCityModel)EditorGUILayout.ObjectField(
                        "エクスポート対象", this.exportTarget,
                        typeof(PLATEAUInstancedCityModel), true);
            }
            /*
            PlateauEditorStyle.Heading("出力形式", "num2.png");
            using (PlateauEditorStyle.VerticalScopeLevel1())
            {
                this.meshFileFormat = (MeshFileFormat)EditorGUILayout.EnumPopup("出力形式", this.meshFileFormat);
            }
            */

            this.foldOutOption = PlateauEditorStyle.FoldOut(this.foldOutOption, "Option", () =>
            {
                using (PlateauEditorStyle.VerticalScopeLevel1())
                {
                    // 選択した出力設定に固有の設定
                    // this.formatToExporter[this.meshFileFormat].DrawConfigGUI();

                    this.exportTextures = EditorGUILayout.Toggle("テクスチャ", this.exportTextures);
                    this.exportHiddenObject = EditorGUILayout.Toggle("非アクティブオブジェクトを含める", this.exportHiddenObject);

                    // this.meshTransformType =
                    //     (MeshExportOptions.MeshTransformType)EditorGUILayout.EnumPopup("座標変換", this.meshTransformType);


                    // this.meshAxis = meshAxisChoices[EditorGUILayout.Popup("座標軸", meshAxisChoices.IndexOf(this.meshAxis), meshAxisDisplay)];
                }
            });

            this.foldOutExportPath = PlateauEditorStyle.FoldOut(this.foldOutExportPath, "出力フォルダ", () =>
            {
                this.exportDirPath = this.exportDirSelector.Draw("フォルダパス");
            });

            PlateauEditorStyle.Separator(0);
            if (PlateauEditorStyle.MainButton("エクスポート"))
            {
                Export(this.exportDirPath, this.exportTarget);
            }
        }

        // FIXME 出力したファイルパスのリストを返すようにできるか？
        private void Export(string destinationDir, PLATEAUInstancedCityModel target)
        {
            if (target == null)
            {
                Debug.LogError("エクスポート対象が指定されていません。");
                return;
            }

            if (string.IsNullOrEmpty(destinationDir))
            {
                Debug.LogError("エクスポート先が指定されていません。");
                return;
            }

            if (!Directory.Exists(destinationDir))
            {
                Debug.LogError("エクスポート先フォルダが実在しません。");
                return;
            }
            var meshExportOptions = new MeshExportOptions(this.meshTransformType, this.exportTextures, this.exportHiddenObject,
                MeshFileFormat.GLTF, this.meshAxis, null);
            // UnityModelExporter.Export(destinationDir, target, meshExportOptions);
            PomlExporter.Export(destinationDir, target, meshExportOptions);
        }
    }
}
