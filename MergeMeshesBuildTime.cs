using System.Collections.Generic;
using System.IO;
using System.Linq;
using Castle.Components.DictionaryAdapter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tools
{
    public class MergeMeshesAtBuildTime
    {
        string[] searchFolders = new[] {"Assets/Content"};
        const string searchString = "prefab_mesh_";

        public static void StartMergeProcess()
        {
            var backgrounds = GetAllBackgroundPrefabs();
            foreach (var background in backgrounds)
            {
                MergeProcess(background);
            }
        }

        private static IEnumerable<Object> GetAllBackgroundPrefabs()
        {
            List<Object> allPrefabsList = new List<Object>();
            string[] assetsGUIDs = AssetDatabase.FindAssets(searchString, searchFolders);

            foreach (string stgGuid in assetsGUIDs)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(stgGuid);
                var asset = AssetDatabase.LoadAssetAtPath(assetPath, typeof(MyCustomComponentInPrefab));
                allPrefabsList.Add(asset);
            }
            return allPrefabsList;
        }

        private static void MergeProcess(Object background)
        {
            string backgroundGameObject = AssetDatabase.FindAssets(background.name)[0];
            string backgroundPath = AssetDatabase.GUIDToAssetPath(backgroundGameObject);

            List<MeshFilter> meshFiltersShadows = new EditableList<MeshFilter>();
            List<MeshFilter> meshFiltersNoShadows = new EditableList<MeshFilter>();
            List<MeshFilter> meshFiltersShadowsOnly = new EditableList<MeshFilter>();
            
            var prefabContents = PrefabUtility.LoadPrefabContents(backgroundPath);
            if (prefabContents.GetComponentsInChildren<MeshFilter>().Length == 0)
            {
                return;
            }
            UnpackPrefabContents(prefabContents);
            
            PrefabUtility.SaveAsPrefabAsset(prefabContents, backgroundPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);
            
            var prefabContentsNew = PrefabUtility.LoadPrefabContents(backgroundPath);
            
            MeshFilter[] meshFilters = prefabContentsNew.GetComponentsInChildren<MeshFilter>();
            if (meshFilters.Length == 0)
            {
                return;
            }

            var meshFiltersList = meshFilters.ToList();
            foreach (MeshFilter filter in meshFiltersList)
            {
                if (filter.gameObject.GetComponent<Renderer>().shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    meshFiltersShadowsOnly.Add(filter);
                }
                if (filter.gameObject.GetComponent<Renderer>().shadowCastingMode == ShadowCastingMode.On)
                {
                    meshFiltersShadows.Add(filter);
                }
                if (filter.gameObject.GetComponent<Renderer>().shadowCastingMode == ShadowCastingMode.Off)
                {
                    meshFiltersNoShadows.Add(filter);
                }
            }

            if (meshFiltersShadowsOnly.Count != 0)
            {
                List<GameObject> combinedObjectsShadowsOnly = CombinePrefabMeshes(meshFiltersShadowsOnly, prefabContentsNew, backgroundPath, ShadowCastingMode.ShadowsOnly, "CombinedMeshesShadowsOnly_");
                if (combinedObjectsShadowsOnly.Count != 0)
                {
                    ParentMeshesToPrefab(combinedObjectsShadowsOnly, prefabContentsNew, "CombinedMeshesShadowsOnly_");
                    DeletePrefabMeshes(meshFiltersShadowsOnly);    
                }    
            }
            
            if (meshFiltersShadows.Count != 0)
            {
                List<GameObject> combinedObjectsShadows = CombinePrefabMeshes(meshFiltersShadows, prefabContentsNew, backgroundPath, ShadowCastingMode.On, "CombinedMeshesShadows_");
                if (combinedObjectsShadows.Count != 0)
                {
                    ParentMeshesToPrefab(combinedObjectsShadows, prefabContentsNew, "CombinedMeshesShadows_");
                    DeletePrefabMeshes(meshFiltersShadows);    
                }    
            }
            
            if (meshFiltersNoShadows.Count != 0)
            {
                List<GameObject> combinedObjectsNoShadows = CombinePrefabMeshes(meshFiltersNoShadows, prefabContentsNew, backgroundPath, ShadowCastingMode.Off, "CombinedMeshesNoShadows_");
                if (combinedObjectsNoShadows.Count != 0)
                {
                    ParentMeshesToPrefab(combinedObjectsNoShadows, prefabContentsNew, "CombinedMeshesNoShadows_");
                    DeletePrefabMeshes(meshFiltersNoShadows);    
                }    
            }
            
            PrefabUtility.SaveAsPrefabAsset(prefabContentsNew, backgroundPath);
            PrefabUtility.UnloadPrefabContents(prefabContentsNew);
        }

        private static void UnpackPrefabContents(GameObject prefab)
        {
            UnpackPrefabRecursive(prefab);
        }

        private static void UnpackPrefabRecursive(GameObject prefabGo)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(prefabGo))
            {
                PrefabUtility.UnpackPrefabInstance(prefabGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            for (int i = 0; i < prefabGo.transform.childCount; i++)
            {
                var childGo = prefabGo.transform.GetChild(i).gameObject;
                if (PrefabUtility.IsPartOfPrefabInstance(childGo))
                {
                    PrefabUtility.UnpackPrefabInstance(childGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                UnpackPrefabRecursive(childGo);
            }
        }
        
        private static void ParentMeshesToPrefab(IReadOnlyList<GameObject> combinedObjects, GameObject prefabContents, string prefix)
        {
            GameObject resultGO;
            if (combinedObjects.Count > 1)
            {
                resultGO = new GameObject(prefix + prefabContents.name);
                foreach (var combinedObject in combinedObjects)
                {
                    combinedObject.transform.parent = resultGO.transform;
                }
            }
            else
            {
                resultGO = combinedObjects[0];
            }

            resultGO.transform.SetParent(prefabContents.gameObject.transform);
        }

        private static void DeletePrefabMeshes(IEnumerable<MeshFilter> meshes)
        {
            foreach (MeshFilter msh in meshes)
            {
                if (!msh)
                {
                    continue;
                }
                Object.DestroyImmediate(msh.gameObject, true);
            }
        }

        private static List<GameObject> CombinePrefabMeshes(IReadOnlyList<MeshFilter> meshFilters, Object prefabContents, string backgroundPath, ShadowCastingMode castShadows, string prefix="CombinedMeshes_")
        {
            Dictionary<Material, List<MeshFilter>> materialToMeshFilterList = new Dictionary<Material, List<MeshFilter>>();
            List<GameObject> combinedObjects = new List<GameObject>();

            for (var i = 0; i < meshFilters.Count; i++)
            {
                if (meshFilters[i] == null)
                {
                    continue;
                }
                
                if (meshFilters[i].sharedMesh != null)
                {
                    var materials = meshFilters[i].GetComponent<MeshRenderer>().sharedMaterials;
                    var material = materials[0];

                    if (materialToMeshFilterList.ContainsKey(material))
                    {
                        materialToMeshFilterList[material].Add(meshFilters[i]);
                    }
                    else
                    {
                        materialToMeshFilterList.Add(material, new List<MeshFilter>() {meshFilters[i]});
                    }
                }
            }
            
            foreach (var entry in materialToMeshFilterList)
            {
                // Get list of each meshes order by number of vertices
                List<MeshFilter> meshesWithSameMaterial = entry.Value.ToList();

                // split into bins of 65536 vertices max
                while (meshesWithSameMaterial.Count > 0)
                {
                    string materialName = entry.Key.ToString().Split(' ')[0];
                    List<MeshFilter> meshBin = new List<MeshFilter> {meshesWithSameMaterial[0]};
                    meshesWithSameMaterial.RemoveAt(0);

                    // add meshes in bin until 65536 vertices is reached
                    for (var i = 0; i < meshesWithSameMaterial.Count; i++)
                    {
                        if (meshBin.Sum(mf => mf.sharedMesh.vertexCount) + meshesWithSameMaterial[i].sharedMesh.vertexCount >= 65536)
                        {
                            continue;
                        }
                        meshBin.Add(meshesWithSameMaterial[i]);
                        meshesWithSameMaterial.RemoveAt(i);
                        i--;
                    }

                    // merge this bin
                    CombineInstance[] combine = new CombineInstance[meshBin.Count];
                    for (var i = 0; i < meshBin.Count; i++)
                    {
                        combine[i].mesh = meshBin[i].sharedMesh;
                        combine[i].transform = meshBin[i].transform.localToWorldMatrix;
                    }

                    Mesh combinedMesh = new Mesh();
                    combinedMesh.CombineMeshes(combine);

                    // save the mesh
                    materialName += "_" + combinedMesh.GetInstanceID();
                    if (meshBin.Count > 1)
                    {
                        string newAssetName = Path.GetDirectoryName(backgroundPath) + "/" + Path.GetFileNameWithoutExtension(backgroundPath) + materialName + ".asset";
                        AssetDatabase.CreateAsset(combinedMesh, newAssetName);
                    }

                    // assign the mesh to a new go
                    string goName = (materialToMeshFilterList.Count > 1) ? prefix + materialName : prefix + prefabContents.name;
                    GameObject combinedObject = new GameObject(goName) {layer = 9};
                    var filter = combinedObject.AddComponent<MeshFilter>();
                    if (meshBin.Count > 1)
                    {
                        filter.sharedMesh = combinedMesh;
                    }
                    else
                    {
                        filter.sharedMesh = meshBin[0].sharedMesh; // the original mesh
                        filter.transform.position = meshBin[0].transform.position;
                    }

                    var renderer = combinedObject.AddComponent<MeshRenderer>();
                    
                    switch (castShadows)
                    {
                        case ShadowCastingMode.On:
                            renderer.shadowCastingMode = ShadowCastingMode.On;
                            break;
                        case ShadowCastingMode.Off:
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                            break;
                        default:
                            renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                            break;
                    }
                    
                    renderer.sharedMaterial = entry.Key;

                    combinedObjects.Add(combinedObject);
                }
            }
            return combinedObjects;
        }
    }
}
